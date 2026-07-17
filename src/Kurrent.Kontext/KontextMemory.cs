// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Microsoft.Extensions.VectorData;

namespace Kurrent.Kontext;

/// <summary>
/// Bare-bones <see cref="IKontextMemory"/> over the <see cref="VectorStore"/> abstraction: one
/// "memories" collection holds the folded state of every memory, and all persistence goes through
/// the portable collection surface (keyed CRUD, filtered listing, vector search), so the backing
/// store can be swapped without touching this class. Embeddings are generated from the memory
/// content by whatever <c>IEmbeddingGenerator</c> the host configured on the store
/// (<see cref="MemoryRecord.Embedding"/>).
///
/// Known simplifications of this skeleton:
/// - Recall scores are raw vector similarity — the recency/importance/certainty ranking
///   (resources.proto ScoringConfig) is not built yet, so <c>min_score</c> cuts on similarity alone.
/// - There is no event log: retract reasons have no home, and nothing is emitted for
///   events.proto consumers.
/// - Reconsolidation touches re-upsert whole records, which regenerates their embeddings (the cost
///   of the string-typed vector property). The opt-in touch buffer
///   (<see cref="KontextMemoryOptions.TouchBuffer"/>) coalesces and defers them off the request
///   path, but the flush still re-embeds.
/// - Reflect is not implemented — it synthesizes derived memories with a language model, which is
///   outside the vector-store surface.
/// </summary>
public sealed class KontextMemory : IKontextMemory, IDisposable, IAsyncDisposable {
	const string CollectionName = "memories";

	const int DefaultRecallLimit    = 10;
	const int DefaultRecollectLimit = 100;

	// Reconcile is an advisory nearest-neighbours peek, not a search the agent controls — keep it small.
	const int ReconcileRelatedLimit = 5;

	// The retract cascade fetches derived memories one generation at a time; a memory with more
	// direct derivations than this loses the tail.
	const int CascadeFetchLimit = 1_000;

	public KontextMemory(VectorStore store, KontextMemoryOptions? options = null) {
		Collection = store.GetCollection<string, MemoryRecord>(CollectionName);

		// The touch buffer is opt-in; without it, reconsolidation writes stay on the request path.
		// Null options mean defaults, and the default is disabled.
		if (options?.TouchBuffer is { Enabled: true } buffering)
			_touchBuffer = new TouchBuffer(Collection, buffering);
	}

	VectorStoreCollection<string, MemoryRecord> Collection { get; }

	readonly TouchBuffer? _touchBuffer;

	volatile bool _collectionReady;

	// EnsureCollectionExistsAsync is idempotent, so the benign race between concurrent first calls
	// is fine — the flag only spares the round-trip on every subsequent operation, and a failure is
	// never cached.
	async ValueTask EnsureCollection(CancellationToken ct) {
		if (_collectionReady)
			return;

		await Collection.EnsureCollectionExistsAsync(ct).ConfigureAwait(false);
		_collectionReady = true;
	}

	public async ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) {
		await EnsureCollection(ct).ConfigureAwait(false);

		var now      = DateTimeOffset.UtcNow;
		var response = new Contracts.RetainResponse();

		// Everything this request writes — the new memories plus any superseded records marked along
		// the way — accumulates here, keyed by id, and lands in ONE batch upsert at the end, so the
		// connector commits once and generates embeddings once. The dictionary doubles as the
		// in-request lookup: supersede targets and id-collision checks must see memories retained
		// earlier in this same batch, which are not in the store yet.
		var flush = new Dictionary<string, MemoryRecord>();

		foreach (var memory in request.Memories) {
			var suppliedId = memory.MemoryId.Length > 0;
			var memoryId   = suppliedId ? memory.MemoryId : Guid.CreateVersion7().ToString();

			// A supplied id must be NEW — one that exists in the store or earlier in this batch is
			// rejected, never silently merged; replacement goes through `supersedes` (memory.proto).
			if (suppliedId && (flush.ContainsKey(memoryId) || await Collection.GetAsync(memoryId, cancellationToken: ct).ConfigureAwait(false) is not null))
				throw new RpcException(new Status(
					StatusCode.AlreadyExists,
					$"Memory '{memoryId}' already exists. To replace it, retain a successor with supersedes."));

			var result = new Contracts.RetainResponse.Types.RetainResult { MemoryId = memoryId };

			// Reconcile is advisory and never blocks the write. It searches stored memories only:
			// siblings pending in this batch can't surface as look-alikes — the caller just wrote
			// them in the same request and needs no reminder.
			if (request.Reconcile) {
				var lookAlikes = Collection.SearchAsync(
					memory.Content, ReconcileRelatedLimit,
					new() { Filter = MemoryRecordFilters.Recall([]) }, ct);

				await foreach (var hit in lookAlikes.ConfigureAwait(false))
					result.Related.Add(new Contracts.RetainResponse.Types.RelatedMemory {
						MemoryId   = hit.Record.MemoryId,
						Similarity = hit.Score ?? 0,
					});
			}

			flush[memoryId] = MemoryRecordMapper.ToRecord(memory, memoryId, now);

			// Supersede is part of retain: the replaced memories stay readable, marked with their
			// successor. Targets may be stored already or pending earlier in this same batch (the
			// proto's "referencing the memory within the same batch"). First marking wins; absent
			// ids and self-references are silently skipped.
			foreach (var supersededId in memory.Supersedes) {
				if (supersededId == memoryId)
					continue;

				var superseded = flush.TryGetValue(supersededId, out var pending)
					? pending
					: await Collection.GetAsync(supersededId, cancellationToken: ct).ConfigureAwait(false);

				if (superseded is null || superseded.IsSuperseded)
					continue;

				superseded.IsSuperseded = true;
				superseded.SupersededAt = now;
				superseded.SupersededBy = memoryId;
				flush[superseded.MemoryId] = superseded;
			}

			response.Results.Add(result);
		}

		if (flush.Count > 0)
			await Collection.UpsertAsync(flush.Values, ct).ConfigureAwait(false);

		return response;
	}

	public async ValueTask<Contracts.RetractResponse> RetractAsync(Contracts.RetractRequest request, CancellationToken ct = default) {
		await EnsureCollection(ct).ConfigureAwait(false);

		var response = new Contracts.RetractResponse();

		// Idempotent: retracting the absent or already-retracted changes nothing. request.Reason has
		// no home in the folded read model — it belongs to the event log the skeleton doesn't have.
		var root = await Collection.GetAsync(request.MemoryId, cancellationToken: ct).ConfigureAwait(false);
		if (root is null || root.IsRetracted)
			return response;

		var now = DateTimeOffset.UtcNow;

		// Breadth-first over the derivation graph, one generation at a time: a derived memory citing
		// a retracted one loses its evidence and is retracted in the same cascade. Each generation is
		// marked and flushed as one batch upsert before its own derivations are looked up. `seen`
		// guards against two parents citing the same derived memory, and against citation cycles.
		var seen       = new HashSet<string> { root.MemoryId };
		var generation = new List<MemoryRecord> { root };

		while (generation.Count > 0) {
			foreach (var record in generation) {
				record.IsRetracted = true;
				record.RetractedAt = now;
				response.RetractedMemoryIds.Add(record.MemoryId);
			}

			await Collection.UpsertAsync(generation, ct).ConfigureAwait(false);

			var next = new List<MemoryRecord>();

			foreach (var record in generation) {
				var retractedId = record.MemoryId;
				var derived = Collection.GetAsync(
					r => r.IsRetracted == false && r.CitedMemoryIds.Contains(retractedId),
					CascadeFetchLimit, cancellationToken: ct);

				await foreach (var d in derived.ConfigureAwait(false))
					if (seen.Add(d.MemoryId))
						next.Add(d);
			}

			generation = next;
		}

		return response;
	}

	public async ValueTask<Contracts.RecallResponse> RecallAsync(Contracts.RecallRequest request, CancellationToken ct = default) {
		await EnsureCollection(ct).ConfigureAwait(false);

		var response = new Contracts.RecallResponse {
			QueryId = request.QueryId.Length > 0 ? request.QueryId : Guid.CreateVersion7().ToString(),
		};

		var top     = request.Limit > 0 ? request.Limit : DefaultRecallLimit;
		var options = new VectorSearchOptions<MemoryRecord> {
			Filter = MemoryRecordFilters.Recall(request.Tags.Select(MemoryRecordMapper.EncodeTag)),
		};

		var recalled = new List<MemoryRecord>();

		await foreach (var hit in Collection.SearchAsync(request.Query, top, options, ct).ConfigureAwait(false)) {
			var score = hit.Score ?? 0;
			if (score < request.MinScore)
				continue;

			var memory = new Contracts.RecallResponse.Types.RecalledMemory { Score = score };

			if (request.IncludeFull)
				memory.Full = MemoryRecordMapper.ToStoredMemory(hit.Record);
			else
				memory.Lean = MemoryRecordMapper.ToLeanMemory(hit.Record);

			response.Memories.Add(memory);
			recalled.Add(hit.Record);
		}

		// Reconsolidation: a recall refreshes the recency clock of everything it returned.
		await TouchAsync(recalled, ct).ConfigureAwait(false);

		return response;
	}

	public async IAsyncEnumerable<Contracts.StoredMemory> ReclaimAsync(Contracts.ReclaimRequest request, [EnumeratorCancellation] CancellationToken ct = default) {
		await EnsureCollection(ct).ConfigureAwait(false);

		// Reclaim is by exact id and never hides: retracted and superseded records come back too;
		// ids that don't exist are simply absent from the stream. Returned records show the recency
		// clock as it was — the refresh below applies from the next access on.
		var reclaimed = new List<MemoryRecord>();

		await foreach (var record in Collection.GetAsync(request.Ids, cancellationToken: ct).ConfigureAwait(false)) {
			reclaimed.Add(record);
			yield return MemoryRecordMapper.ToStoredMemory(record);
		}

		await TouchAsync(reclaimed, ct).ConfigureAwait(false);
	}

	public async IAsyncEnumerable<Contracts.StoredMemory> RecollectAsync(Contracts.RecollectRequest request, [EnumeratorCancellation] CancellationToken ct = default) {
		await EnsureCollection(ct).ConfigureAwait(false);

		var top        = request.Limit > 0 ? request.Limit : DefaultRecollectLimit;
		var tags       = request.Tags.Select(MemoryRecordMapper.EncodeTag).ToList();
		var descending = request.Direction != Contracts.SortDirection.Ascending;

		var options = new FilteredRecordRetrievalOptions<MemoryRecord> {
			OrderBy = order => descending ? order.Descending(SortKey(request.Sort)) : order.Ascending(SortKey(request.Sort)),
		};

		// The portable filter surface has no OR, so the any-of type set becomes one query per type.
		// Each query is already sorted and limited server-side; the merge re-sorts and re-limits.
		List<int?> typeFilters = request.Types_.Count == 0
			? [null]
			: [.. request.Types_.Distinct().Select(type => (int?)(int)type)];

		var merged = new List<MemoryRecord>();

		foreach (var typeFilter in typeFilters) {
			var records = Collection.GetAsync(MemoryRecordFilters.Recollect(tags, typeFilter), top, options, ct);
			await foreach (var record in records.ConfigureAwait(false))
				merged.Add(record);
		}

		var compareKey = CompareKey(request.Sort);
		var ordered    = descending ? merged.OrderByDescending(compareKey) : merged.OrderBy(compareKey);

		foreach (var record in ordered.Take(top))
			yield return MemoryRecordMapper.ToStoredMemory(record);
	}

	public ValueTask<Contracts.ReflectResponse> ReflectAsync(Contracts.ReflectRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Reflect synthesizes derived memories with a language model — not part of the vector-store skeleton.");

	// Reconsolidation write-back: advance the recency clock on every returned memory — the only
	// write in the recency mechanism (resources.proto ScoredMemory.last_accessed_at). With the
	// opt-in buffer the touches coalesce off the request path; unbuffered, they land before the
	// operation returns.
	async ValueTask TouchAsync(IReadOnlyList<MemoryRecord> records, CancellationToken ct) {
		if (records.Count == 0)
			return;

		var now = DateTimeOffset.UtcNow;

		if (_touchBuffer is not null) {
			_touchBuffer.Add(records, now);
			return;
		}

		foreach (var record in records)
			record.LastAccessedAt = now;

		await Collection.UpsertAsync(records, ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Flushes any buffered touches. Disposal is an implementation concern of the buffering, so it
	/// lives on the class, not on <see cref="IKontextMemory"/>. The sync path is best-effort (the
	/// remaining flush is fired without being awaited); prefer async disposal.
	/// </summary>
	public ValueTask DisposeAsync() => _touchBuffer?.DisposeAsync() ?? ValueTask.CompletedTask;

	public void Dispose() => _touchBuffer?.Dispose();

	// Server-side ordering key, in the abstraction's boxed OrderBy shape.
	static Expression<Func<MemoryRecord, object?>> SortKey(Contracts.RecollectSort sort) => sort switch {
		Contracts.RecollectSort.LastAccessedAt => r => r.LastAccessedAt,
		Contracts.RecollectSort.Importance     => r => r.Importance,
		_                                      => r => r.RetainedAt, // default: when the memory was recorded
	};

	// Client-side key for merging the per-type queries — must order exactly like SortKey.
	static Func<MemoryRecord, IComparable> CompareKey(Contracts.RecollectSort sort) => sort switch {
		Contracts.RecollectSort.LastAccessedAt => r => r.LastAccessedAt,
		Contracts.RecollectSort.Importance     => r => r.Importance,
		_                                      => r => r.RetainedAt,
	};
}
