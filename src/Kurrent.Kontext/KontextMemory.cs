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
///   of the string-typed vector property).
/// - Reflect is not implemented — it synthesizes derived memories with a language model, which is
///   outside the vector-store surface.
/// </summary>
public sealed class KontextMemory(VectorStore store) : IKontextMemory {
	const string CollectionName = "memories";

	const int DefaultRecallLimit    = 10;
	const int DefaultRecollectLimit = 100;

	// Reconcile is an advisory nearest-neighbours peek, not a search the agent controls — keep it small.
	const int ReconcileRelatedLimit = 5;

	// The retract cascade fetches derived memories one generation at a time; a memory with more
	// direct derivations than this loses the tail.
	const int CascadeFetchLimit = 1_000;

	VectorStoreCollection<string, MemoryRecord> Collection { get; } =
		store.GetCollection<string, MemoryRecord>(CollectionName);

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

		// Sequential on purpose: memories in one batch may supersede ids retained earlier in the same batch.
		foreach (var memory in request.Memories) {
			var suppliedId = memory.MemoryId.Length > 0;
			var memoryId   = suppliedId ? memory.MemoryId : Guid.CreateVersion7().ToString();

			// A supplied id must be NEW — an existing one is rejected, never silently merged;
			// replacement goes through `supersedes` (memory.proto Retain).
			if (suppliedId && await Collection.GetAsync(memoryId, cancellationToken: ct).ConfigureAwait(false) is not null)
				throw new RpcException(new Status(
					StatusCode.AlreadyExists,
					$"Memory '{memoryId}' already exists. To replace it, retain a successor with supersedes."));

			var result = new Contracts.RetainResponse.Types.RetainResult { MemoryId = memoryId };

			// Reconcile searches before the upsert so the new memory can never match itself. It is
			// advisory and never blocks the write.
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

			await Collection.UpsertAsync(MemoryRecordMapper.ToRecord(memory, memoryId, now), ct).ConfigureAwait(false);

			// Supersede is part of retain: the replaced memories stay readable, marked with their
			// successor. First marking wins; ids that don't exist are silently skipped.
			foreach (var supersededId in memory.Supersedes) {
				var superseded = await Collection.GetAsync(supersededId, cancellationToken: ct).ConfigureAwait(false);
				if (superseded is null || superseded.IsSuperseded)
					continue;

				superseded.IsSuperseded = true;
				superseded.SupersededAt = now;
				superseded.SupersededBy = memoryId;
				await Collection.UpsertAsync(superseded, ct).ConfigureAwait(false);
			}

			response.Results.Add(result);
		}

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

		// Breadth-first over the derivation graph: a derived memory citing a retracted one loses its
		// evidence and is retracted in the same cascade. `seen` guards against two parents citing
		// the same derived memory, and against citation cycles.
		var seen    = new HashSet<string> { root.MemoryId };
		var pending = new Queue<MemoryRecord>([root]);

		while (pending.TryDequeue(out var record)) {
			record.IsRetracted = true;
			record.RetractedAt = now;
			await Collection.UpsertAsync(record, ct).ConfigureAwait(false);
			response.RetractedMemoryIds.Add(record.MemoryId);

			var retractedId = record.MemoryId;
			var derived = Collection.GetAsync(
				r => r.IsRetracted == false && r.CitedMemoryIds.Contains(retractedId),
				CascadeFetchLimit, cancellationToken: ct);

			await foreach (var next in derived.ConfigureAwait(false))
				if (seen.Add(next.MemoryId))
					pending.Enqueue(next);
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
	// write in the recency mechanism (resources.proto ScoredMemory.last_accessed_at).
	async ValueTask TouchAsync(IReadOnlyList<MemoryRecord> records, CancellationToken ct) {
		if (records.Count == 0)
			return;

		var now = DateTimeOffset.UtcNow;
		foreach (var record in records)
			record.LastAccessedAt = now;

		await Collection.UpsertAsync(records, ct).ConfigureAwait(false);
	}

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
