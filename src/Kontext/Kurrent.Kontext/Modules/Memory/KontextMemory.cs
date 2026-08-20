// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;

namespace Kurrent.Kontext.Memory;

public delegate Task AppendEvent(object evt, CancellationToken ct = default);

/// <summary>
/// The transport-neutral memory service, composed over the projector-owned
/// <see cref="KontextMemoryDataStore"/> read model. The store only READS the lance table the projector
/// writes, so every operation that mutates memory state — retain and the recall
/// reconsolidation touches — is not implemented here: writes go through the KurrentDB log and land
/// in the read model via the projector, a path this service does not own yet.
///
/// Known simplifications of this port:
/// - Recall runs whatever <see cref="IKontextRetriever"/> pipeline the host wired — by default
///   vector+keyword RRF, modulated by the cognitive model, MMR-polished — so <c>min_score</c> cuts
///   on the pipeline's final score scale, not on raw BM25.
/// - No reconsolidation: recall and reclaim do not refresh <c>last_accessed_at</c> — that is a
///   write, and writes belong to the log.
/// - Reflect is not implemented — it synthesizes derived memories with a language model, which is
///   outside the data-store surface.
/// </summary>
public sealed class KontextMemory(KontextMemoryDataStore store, IKontextRetriever retriever, AppendEvent appendEvent) : IKontextMemory {
	const int DefaultRecallLimit    = 10;
	const int DefaultRecollectLimit = 100;

	public AppendEvent AppendEvent { get; } = appendEvent;

	public ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Retain writes memories; the read model is projector-owned — retain goes through the KurrentDB log once the write path lands.");

	public async ValueTask<Contracts.RecallResponse> RecallAsync(Contracts.RecallRequest request, CancellationToken ct = default) {
		var response = new Contracts.RecallResponse {
			QueryId = request.QueryId.Length > 0 ? request.QueryId : Guid.CreateVersion7().ToString(),
		};

		var query = new RetrievalQuery {
			Text     = request.Query,
			Tags     = request.Tags,
			Limit    = request.Limit > 0 ? request.Limit : DefaultRecallLimit,
			MinScore = request.MinScore,
		};

		var ranked = await retriever.RetrieveAsync(query, ct).ConfigureAwait(false);

		foreach (var scored in ranked) {
			var memory = new Contracts.RecallResponse.Types.RecalledMemory { Score = scored.Score };

			if (request.IncludeFull)
				memory.Full = scored.Memory;
			else
				memory.Lean = ToLean(scored.Memory);

			response.Memories.Add(memory);
		}

		return response;

        static Contracts.LeanMemory ToLean(Contracts.StoredMemory stored) {
            var lean = new Contracts.LeanMemory {
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
		store.GetAsync([.. request.Ids], ct);

	public async IAsyncEnumerable<Contracts.StoredMemory> RecollectAsync(Contracts.RecollectRequest request, [EnumeratorCancellation] CancellationToken ct = default) {
		var top = request.Limit > 0 ? request.Limit : DefaultRecollectLimit;

		await foreach (var memory in store.ListAsync(request.Tags, request.Types_, request.Sort, request.Direction, top, ct).ConfigureAwait(false))
			yield return memory;
	}

	public ValueTask<Contracts.ReflectResponse> ReflectAsync(Contracts.ReflectRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Reflect synthesizes derived memories with a language model — not part of the data-store surface.");
}
