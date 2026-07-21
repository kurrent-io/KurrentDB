// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Kurrent.Kontext.Data;

namespace Kurrent.Kontext;

public delegate Task AppendEvent(object evt, CancellationToken ct = default);

/// <summary>
/// The transport-neutral memory service: owns the domain workflows — retain with supersede
/// semantics, the retract cascade, recall and its reconsolidation touches — and composes
/// <see cref="KontextDataStore"/> for ALL persistence. How memories are physically stored and found
/// is the data store's business alone; this class speaks contract types end to end, which keeps it
/// free for the concerns that are NOT storage: appending to the KurrentDB log and richer retrieval
/// pipelines (cross-encoders, reranking) as they arrive.
///
/// Known simplifications of this skeleton:
/// - Recall scores are raw vector similarity — the recency/importance/certainty ranking
///   (resources.proto ScoringConfig) is not built yet, so <c>min_score</c> cuts on similarity alone.
/// - There is no event log: retract reasons have no home, and nothing is emitted for
///   events.proto consumers.
/// - Reflect is not implemented — it synthesizes derived memories with a language model, which is
///   outside the data-store surface.
/// </summary>
public sealed class KontextMemory(KontextDataStore store, AppendEvent appendEvent) : IKontextMemory {
	const int DefaultRecallLimit    = 10;
	const int DefaultRecollectLimit = 100;

	// Reconcile is an advisory nearest-neighbours peek, not a search the agent controls — keep it small.
	const int ReconcileRelatedLimit = 5;

	// The retract cascade fetches derived memories one generation at a time; a memory with more
	// direct derivations than this loses the tail.
	const int CascadeFetchLimit = 1_000;

	public async ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) {
		var now      = DateTimeOffset.UtcNow;
		var response = new Contracts.RetainResponse();

		// Everything this request writes — the new memories plus any superseded ones marked along the
		// way — accumulates here, keyed by id, and lands in ONE batch save at the end. The dictionary
		// doubles as the in-request lookup: supersede targets and id-collision checks must see
		// memories retained earlier in this same batch, which are not in the store yet.
		var flush = new Dictionary<string, Contracts.StoredMemory>();

		foreach (var memory in request.Memories) {
			var suppliedId = memory.MemoryId.Length > 0;
			var memoryId   = suppliedId ? memory.MemoryId : Guid.CreateVersion7().ToString();

			// A supplied id must be NEW — one that exists in the store or earlier in this batch is
			// rejected, never silently merged; replacement goes through `supersedes` (memory.proto).
			if (suppliedId && (flush.ContainsKey(memoryId) || await store.GetAsync(memoryId, ct).ConfigureAwait(false) is not null))
				throw new RpcException(new Status(
					StatusCode.AlreadyExists,
					$"Memory '{memoryId}' already exists. To replace it, retain a successor with supersedes."));

			var result = new Contracts.RetainResponse.Types.RetainResult { MemoryId = memoryId };

			// Reconcile is advisory and never blocks the write. It searches stored memories only:
			// siblings pending in this batch can't surface as look-alikes — the caller just wrote
			// them in the same request and needs no reminder.
			if (request.Reconcile) {
                // this is just a quick implementation cause it simply checks for similarity in the content of the memory, but it can be improved to check for other fields as well
				await foreach (var hit in store.SearchAsync(memory.Content, ReconcileRelatedLimit, [], ct).ConfigureAwait(false))
					result.Related.Add(new Contracts.RetainResponse.Types.RelatedMemory {
						MemoryId   = hit.Memory.MemoryId,
						Similarity = hit.Score,
					});
			}

			flush[memoryId] = ToStored(memory, memoryId, now);

			// Supersede is part of retain: the replaced memories stay readable, marked with their
			// successor. Targets may be stored already or pending earlier in this same batch (the
			// proto's "referencing the memory within the same batch"). First marking wins; absent
			// ids and self-references are silently skipped.
			foreach (var supersededId in memory.Supersedes) {
				if (supersededId == memoryId)
					continue;

				var superseded = flush.TryGetValue(supersededId, out var pending)
					? pending
					: await store.GetAsync(supersededId, ct).ConfigureAwait(false);

				if (superseded is null || superseded.SupersededAt is not null)
					continue;

				superseded.SupersededAt = Timestamp.FromDateTimeOffset(now);
				superseded.SupersededBy = memoryId;
				flush[superseded.MemoryId] = superseded;
			}

			response.Results.Add(result);
		}

		await store.SaveAsync(flush.Values, ct).ConfigureAwait(false);

		return response;
	}

	public async ValueTask<Contracts.RetractResponse> RetractAsync(Contracts.RetractRequest request, CancellationToken ct = default) {
		var response = new Contracts.RetractResponse();

		// Idempotent: retracting the absent or already-retracted changes nothing. request.Reason has
		// no home in the folded read model — it belongs to the event log the skeleton doesn't have.
		var root = await store.GetAsync(request.MemoryId, ct).ConfigureAwait(false);
		if (root is null || root.RetractedAt is not null)
			return response;

		var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

		// Breadth-first over the derivation graph, one generation at a time: a derived memory citing
		// a retracted one loses its evidence and is retracted in the same cascade. Each generation is
		// marked and flushed as one batch save before its own derivations are looked up. `seen`
		// guards against two parents citing the same derived memory, and against citation cycles.
		var seen       = new HashSet<string> { root.MemoryId };
		var generation = new List<Contracts.StoredMemory> { root };

		while (generation.Count > 0) {
			foreach (var memory in generation) {
				memory.RetractedAt = now;
				response.RetractedMemoryIds.Add(memory.MemoryId);
			}

			await store.SaveAsync(generation, ct).ConfigureAwait(false);

			var next = new List<Contracts.StoredMemory>();

			foreach (var memory in generation) {
				await foreach (var derived in store.FindDerivedAsync(memory.MemoryId, CascadeFetchLimit, ct).ConfigureAwait(false))
					if (seen.Add(derived.MemoryId))
						next.Add(derived);
			}

			generation = next;
		}

		return response;
	}

	public async ValueTask<Contracts.RecallResponse> RecallAsync(Contracts.RecallRequest request, CancellationToken ct = default) {
		var response = new Contracts.RecallResponse {
			QueryId = request.QueryId.Length > 0 ? request.QueryId : Guid.CreateVersion7().ToString(),
		};

		var top      = request.Limit > 0 ? request.Limit : DefaultRecallLimit;
		var recalled = new List<Contracts.StoredMemory>();

		await foreach (var hit in store.SearchAsync(request.Query, top, request.Tags, ct).ConfigureAwait(false)) {
			if (hit.Score < request.MinScore)
				continue;

			var memory = new Contracts.RecallResponse.Types.RecalledMemory { Score = hit.Score };

			if (request.IncludeFull)
				memory.Full = hit.Memory;
			else
				memory.Lean = ToLean(hit.Memory);

			response.Memories.Add(memory);
			recalled.Add(hit.Memory);
		}

		// Reconsolidation: a recall refreshes the recency clock of everything it returned — the only
		// write in the recency mechanism (resources.proto ScoredMemory.last_accessed_at).
		await store.MarkAccessedAsync(recalled, ct).ConfigureAwait(false);

		return response;
	}

	public async IAsyncEnumerable<Contracts.StoredMemory> ReclaimAsync(Contracts.ReclaimRequest request, [EnumeratorCancellation] CancellationToken ct = default) {
		// Reclaim is by exact id and never hides: retracted and superseded memories come back too;
		// ids that don't exist are simply absent from the stream. Returned memories show the recency
		// clock as it was — the refresh below applies from the next access on.
		var reclaimed = new List<Contracts.StoredMemory>();

		await foreach (var memory in store.GetAsync(request.Ids, ct).ConfigureAwait(false)) {
			reclaimed.Add(memory);
			yield return memory;
		}

		await store.MarkAccessedAsync(reclaimed, ct).ConfigureAwait(false);
	}

	public async IAsyncEnumerable<Contracts.StoredMemory> RecollectAsync(Contracts.RecollectRequest request, [EnumeratorCancellation] CancellationToken ct = default) {
		var top = request.Limit > 0 ? request.Limit : DefaultRecollectLimit;

		await foreach (var memory in store.ListAsync(request.Tags, request.Types_, request.Sort, request.Direction, top, ct).ConfigureAwait(false))
			yield return memory;
	}

	public ValueTask<Contracts.ReflectResponse> ReflectAsync(Contracts.ReflectRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Reflect synthesizes derived memories with a language model — not part of the data-store skeleton.");

	// The retain stamping: a command Memory becomes the stored shape with its server-assigned id and
	// both clocks set to the retain instant.
	static Contracts.StoredMemory ToStored(Contracts.Memory memory, string memoryId, DateTimeOffset now) {
		var stored = new Contracts.StoredMemory {
			MemoryId       = memoryId,
			MemoryType     = memory.MemoryType,
			Content        = memory.Content,
			Importance     = memory.Importance,
			Sentiment      = memory.Sentiment,
			Urgency        = memory.Urgency,
			RetainedAt     = Timestamp.FromDateTimeOffset(now),
			LastAccessedAt = Timestamp.FromDateTimeOffset(now),
		};

		stored.Tags.AddRange(memory.Tags);
		stored.Supersedes.AddRange(memory.Supersedes);

		if (memory.Evidence is not null)
			stored.Evidence = memory.Evidence;

		if (memory.Validity is not null)
			stored.Validity = memory.Validity;

		return stored;
	}

	// Lean recall drops the heavy fields (evidence, validity, lifecycle stamps) — a projection over
	// contract types only; the data store is not involved.
	static Contracts.RecallResponse.Types.RecalledMemory.Types.LeanMemory ToLean(Contracts.StoredMemory stored) {
		var lean = new Contracts.RecallResponse.Types.RecalledMemory.Types.LeanMemory {
			MemoryId   = stored.MemoryId,
			MemoryType = stored.MemoryType,
			Content    = stored.Content,
			Importance = stored.Importance,
			RetainedAt = stored.RetainedAt,
		};

		lean.Tags.AddRange(stored.Tags);
		return lean;
	}
}
