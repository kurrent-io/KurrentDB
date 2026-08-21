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
	/// Stores every memory that is not already live in exactly this form, then appends ONE event
	/// for what it wrote. A written memory becomes recallable when the projector applies that
	/// event, so this stays eventually consistent: retain-then-recall in the same breath finds
	/// nothing.
	/// </summary>
	public async ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) {
		var response = new Contracts.RetainResponse();

		var retained = new Contracts.MemoriesRetained {
			RetainedAt = Timestamp.FromDateTimeOffset(time.GetUtcNow()),
		};

		// Results are appended in lockstep with the request because the contract promises
		// results[i] is the memory sent at memories[i].
		foreach (var memory in request.Memories) {
			// The one write retain refuses: a memory already live in exactly this form. That is an
			// idempotency guard against a resend, NOT deduplication — anything less than identical
			// is stored, because deciding otherwise means guessing which tags and which citations
			// the caller meant to keep.
			if (await FindIdenticalAsync(memory, ct).ConfigureAwait(false) is { } identical) {
				response.Results.Add(new Contracts.RetainResponse.Types.RetainResult {
					Outcome  = Contracts.RetainOutcome.Noop,
					MemoryId = identical.MemoryId,
				});

				continue;
			}

			// The server mints every id — a caller can neither collide with an existing memory nor
			// cite one it is sending in this same batch.
			var memoryId = Guid.CreateVersion7().ToString();

			retained.Memories.Add(new Contracts.MemoriesRetained.Types.RetainedMemory { MemoryId = memoryId, Memory = memory });

			response.Results.Add(new Contracts.RetainResponse.Types.RetainResult {
				Outcome  = Contracts.RetainOutcome.Created,
				MemoryId = memoryId,
			});
		}

		await ReportNeighboursAsync(request, response, ct).ConfigureAwait(false);

		// ONE event per call, never one per memory: the batch is the unit of the operation, so a
		// reader can never observe half of it and the projector pays one commit instead of N. A
		// batch that only NOOPed wrote nothing, and an event for it would carry nothing.
		if (retained.Memories.Count > 0)
			await appendEvent(retained, ct).ConfigureAwait(false);

		return response;
	}

	/// <summary>The live memory already holding exactly this content, tags and evidence, or null.</summary>
	async ValueTask<Contracts.StoredMemory?> FindIdenticalAsync(Contracts.Memory memory, CancellationToken ct) {
		if (await store.FindLiveByContentAsync(memory.Content, ct).ConfigureAwait(false) is not { } existing)
			return null;

		var tags = existing.Tags.Select(KontextMemoryDataStore.EncodeTag).ToHashSet();

		return tags.SetEquals(memory.Tags.Select(KontextMemoryDataStore.EncodeTag))
		    && existing.Evidence.ToHashSet().SetEquals(memory.Evidence)
			? existing
			: null;
	}

	/// <summary>
	/// Reports the live memories nearest each one just stored, when the caller asked for them.
	/// <para>Advisory and after the fact: nothing here blocks, changes or is owed an answer. The
	/// embedding is the only reason retain touches a model at all — the projector owns the vectors
	/// the store keeps — so leaving <c>neighbours</c> at 0 costs nothing.</para>
	/// </summary>
	async ValueTask ReportNeighboursAsync(Contracts.RetainRequest request, Contracts.RetainResponse response, CancellationToken ct) {
		var wanted = Math.Min(request.Neighbours, options.MaxNeighbours);

		if (wanted <= 0 || !response.Results.Any(result => result.Outcome == Contracts.RetainOutcome.Created))
			return;

		// ONE embedding call for the batch. The local ONNX generator runs one session per string
		// however many it is handed, but the remote generators batch over HTTP, where this is one
		// round trip instead of N. A NOOP's vector goes unused, which costs less than the
		// bookkeeping to exclude something that only fires on a resend.
		var vectors    = await embeddings.GenerateAsync(request.Memories.Select(memory => memory.Content), EmbeddingOptions, ct).ConfigureAwait(false);
		var searchOpts = new HybridSearchOptions { K = wanted, Alpha = options.NeighbourAlpha };

		for (var i = 0; i < request.Memories.Count; i++) {
			if (response.Results[i].Outcome != Contracts.RetainOutcome.Created)
				continue;

			var memory     = request.Memories[i];
			var neighbours = new List<Contracts.RetainResponse.Types.Neighbour>(wanted);

			// Scoped by the memory's own tags. Once the server stamps `user`, that is what keeps the
			// search from crossing principals; an untagged memory is searched unscoped.
			await foreach (var hit in store.SearchAsync(memory.Content, vectors[i].Vector.ToArray(), memory.Tags, searchOpts, ct).ConfigureAwait(false))
				neighbours.Add(new Contracts.RetainResponse.Types.Neighbour {
					// Infinity when the vector leg never placed this row: the keyword leg alone
					// found it, and reporting 0 would read as identical, and it sorts last.
					Distance     = hit.VectorDistance ?? double.PositiveInfinity,
					KeywordMatch = hit.KeywordScore is not null,
					Memory       = ToLean(hit.Memory),
				});

			// Re-sorted, because the engine orders by its blended score and that is not a similarity
			// ordering: a keyword hit on a shared word outranks a far closer vector match. The
			// contract promises nearest first, so distance decides.
			response.Results[i].Neighbours.AddRange(neighbours.OrderBy(neighbour => neighbour.Distance));
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

	/// <summary>The lean projection both recall hits and retain's candidates are reported as.</summary>
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
