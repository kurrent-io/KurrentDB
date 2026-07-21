// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Kurrent.Kontext.Data;

namespace Kurrent.Kontext;

public delegate Task AppendEvent(object evt, CancellationToken ct = default);

/// <summary>
/// The transport-neutral memory service, composed over the projector-owned
/// <see cref="KontextDataStore"/> read model. The store only READS the lance table the projector
/// writes, so every operation that mutates memory state — retain, retract, and the recall
/// reconsolidation touches — is not implemented here: writes go through the KurrentDB log and land
/// in the read model via the projector, a path this service does not own yet.
///
/// Known simplifications of this port:
/// - Recall is keyword (BM25) search only: this service has no embedding generator — query
///   embeddings are produced upstream in the retrieval pipeline — so the vector and hybrid legs of
///   the store are not reachable from here yet, and <c>min_score</c> cuts on the corpus-relative
///   BM25 score (a gate, not a calibrated threshold).
/// - No reconsolidation: recall and reclaim do not refresh <c>last_accessed_at</c> — that is a
///   write, and writes belong to the log.
/// - Reflect is not implemented — it synthesizes derived memories with a language model, which is
///   outside the data-store surface.
/// </summary>
public sealed class KontextMemory(KontextDataStore store, AppendEvent appendEvent) : IKontextMemory {
	const int DefaultRecallLimit    = 10;
	const int DefaultRecollectLimit = 100;

	public AppendEvent AppendEvent { get; } = appendEvent;

	public ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Retain writes memories; the read model is projector-owned — retain goes through the KurrentDB log once the write path lands.");

	public ValueTask<Contracts.RetractResponse> RetractAsync(Contracts.RetractRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Retract mutates memories; the read model is projector-owned — retract goes through the KurrentDB log once the write path lands.");

	public async ValueTask<Contracts.RecallResponse> RecallAsync(Contracts.RecallRequest request, CancellationToken ct = default) {
		var response = new Contracts.RecallResponse {
			QueryId = request.QueryId.Length > 0 ? request.QueryId : Guid.CreateVersion7().ToString(),
		};

		var top = request.Limit > 0 ? request.Limit : DefaultRecallLimit;

		// Keyword-only for now (see the class remarks).
		// K rides with the page size so the candidate pool never truncates the requested page.
		var options = new FullTextSearchOptions { Limit = top, K = top };

		await foreach (var hit in store.SearchAsync(request.Query, request.Tags, options, ct).ConfigureAwait(false)) {
			// Only the keyword leg runs, so KeywordScore is always present (see MemoryHit remarks).
			var score = hit.KeywordScore ?? 0;
			if (score < request.MinScore)
				continue;

			var memory = new Contracts.RecallResponse.Types.RecalledMemory { Score = score };

			if (request.IncludeFull)
				memory.Full = hit.Memory;
			else
				memory.Lean = ToLean(hit.Memory);

			response.Memories.Add(memory);
		}

		return response;

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

	public IAsyncEnumerable<Contracts.StoredMemory> ReclaimAsync(Contracts.ReclaimRequest request, CancellationToken ct = default) =>
		store.GetAsync(request.Ids.ToArray(), ct);

	public async IAsyncEnumerable<Contracts.StoredMemory> RecollectAsync(Contracts.RecollectRequest request, [EnumeratorCancellation] CancellationToken ct = default) {
		var top = request.Limit > 0 ? request.Limit : DefaultRecollectLimit;

		await foreach (var memory in store.ListAsync(request.Tags, request.Types_, request.Sort, request.Direction, top, ct).ConfigureAwait(false))
			yield return memory;
	}

	public ValueTask<Contracts.ReflectResponse> ReflectAsync(Contracts.ReflectRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Reflect synthesizes derived memories with a language model — not part of the data-store surface.");
}
