// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Configuration;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Microsoft.Extensions.AI;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;

using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Memory;

public delegate Task AppendEvent(object evt, CancellationToken ct = default);

/// <summary>
/// The transport-neutral memory service, composed over the projector-owned
/// <see cref="KontextMemoryDataStore"/> read model. The store only READS the lance table the
/// projector writes, so nothing here mutates it directly: <c>retain</c> appends to the KurrentDB
/// log and the projector carries it into the read model. That makes retain EVENTUALLY CONSISTENT —
/// a memory is not recallable the instant retain returns its id.
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
public sealed class KontextMemory(
	KontextMemoryDataStore store,
	IKontextRetriever retriever,
	AppendEvent appendEvent,
	TimeProvider time,
	EmbeddingGenerator embeddings,
	KontextMemoryOptions options
) : IKontextMemory {
	const int DefaultRecallLimit    = 10;
	const int DefaultRecollectLimit = 100;

	static readonly EmbeddingGenerationOptions EmbeddingOptions = new() { Dimensions = KontextIndexConstants.VectorsDimension };

	/// <summary>
	/// Mints an id per memory, appends ONE event for the batch, and returns the ids positionally.
	/// The memory becomes recallable only once the projector applies that event, so this is
	/// deliberately eventually consistent: retain-then-recall in the same breath will not find it.
	/// </summary>
	public async ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) {
		var retained = new Contracts.MemoriesRetained {
			RetainedAt = Timestamp.FromDateTimeOffset(time.GetUtcNow()),
		};

		var response = new Contracts.RetainResponse();

		// The server mints every id — a caller can neither collide with an existing memory nor cite
		// one it is sending in this same batch. Both lists are appended in lockstep because the
		// contract promises results[i] is the memory sent at memories[i].
		foreach (var memory in request.Memories) {
			var memoryId = Guid.CreateVersion7().ToString();

			retained.Memories.Add(new Contracts.MemoriesRetained.Types.RetainedMemory { MemoryId = memoryId, Memory = memory });
			response.Results.Add(new Contracts.RetainResponse.Types.RetainResult { MemoryId = memoryId });
		}

		// Neighbours are found BEFORE the append: they describe the incoming memories, and nothing
		// here has been written yet.
		await FindRelatedAsync(request, response, ct).ConfigureAwait(false);

		// ONE event per call, never one per memory: the batch is the unit of the operation, so a
		// reader can never observe half of it and the projector pays one commit instead of N.
		await appendEvent(retained, ct).ConfigureAwait(false);

		return response;
	}

	/// <summary>Finds the closest live memories to each incoming one.</summary>
	async ValueTask FindRelatedAsync(Contracts.RetainRequest request, Contracts.RetainResponse response, CancellationToken ct) {
		if (options.RelatedLimit <= 0)
			return;

		// One call for the batch: the per-item cost is the same, and it keeps model round trips to one.
		var contents   = request.Memories.Select(memory => memory.Content).ToArray();
		var embedded   = await embeddings.GenerateAsync(contents, EmbeddingOptions, ct).ConfigureAwait(false);
		var searchOpts = new HybridSearchOptions { K = options.RelatedLimit, Alpha = options.RelatedAlpha };

		for (var i = 0; i < request.Memories.Count; i++) {
			var memory = request.Memories[i];
			// Scoped by the memory's own tags. Once the server stamps `user`, that is what keeps a
			// neighbour search from crossing principals; an untagged memory is searched unscoped.
			var hits = store.SearchAsync(memory.Content, embedded[i].Vector.ToArray(), memory.Tags, searchOpts, ct);

			await foreach (var hit in hits.ConfigureAwait(false))
				response.Results[i].Related.Add(new Contracts.RetainResponse.Types.RelatedMemory {
					// Rank-shaped: orders neighbours within one search, meaningless as an absolute.
					Similarity = hit.HybridScore ?? 0,
					Memory     = ToLean(hit.Memory),
				});
		}
	}

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
	}

	/// <summary>The lean projection both recall hits and retain's neighbours are reported as.</summary>
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
