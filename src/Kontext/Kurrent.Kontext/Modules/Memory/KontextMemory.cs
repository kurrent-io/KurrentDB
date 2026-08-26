// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation.Results;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Configuration;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.Validation;
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
/// - Reclaim does not refresh <c>last_accessed_at</c>: the ACCESS RULE says it should, but the write
///   is not wired. Recall and reinforce both do.
/// </summary>
public sealed class KontextMemory(
	KontextMemoryDataStore store,
	IKontextRetriever retriever,
	AppendEvent appendEvent,
	TimeProvider time,
	EmbeddingGenerator embeddings,
	KontextMemoryOptions options,
	RequestValidationService validation
) : IKontextMemory {
	const int DefaultRecallLimit    = 10;
	const int DefaultRecollectLimit = 100;

	static readonly EmbeddingGenerationOptions EmbeddingOptions = new() { Dimensions = KontextIndexConstants.VectorsDimension };

	/// <summary>
	/// Stores every memory that is not already live in exactly this form, then appends ONE event
	/// for what it wrote. A written memory becomes recallable when the projector applies that
	/// event, so this stays eventually consistent: retain-then-recall in the same breath finds
	/// nothing.
	/// <para>All-or-nothing on the ids it references: a target that does not resolve rejects the
	/// whole call. One retained moments ago may not be projected yet, and reads as missing until it
	/// lands.</para>
	/// </summary>
	public async ValueTask<Contracts.RetainResponse> RetainAsync(Contracts.RetainRequest request, CancellationToken ct = default) {
		validation.Validate(request);

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

		// Validated over what will actually be WRITTEN, never over the request. A NOOP is a resend
		// of a retain that already succeeded, and its target is by now superseded BY IT — checking
		// it would reject exactly the retry the idempotency guard exists to absorb.
		await EnsureReferencedMemoriesResolveAsync(retained, ct).ConfigureAwait(false);

		await ReportNeighboursAsync(request, response, ct).ConfigureAwait(false);

		// ONE event per call, never one per memory: the batch is the unit of the operation, so a
		// reader can never observe half of it and the projector pays one commit instead of N. A
		// batch that only NOOPed wrote nothing, and an event for it would carry nothing.
		if (retained.Memories.Count > 0)
			await appendEvent(retained, ct).ConfigureAwait(false);

		return response;
	}

	/// <summary>
	/// The live memory already holding exactly this content, tags, evidence and supersessions, or
	/// null. <c>supersedes</c> counts: a fold retains the surviving claim again with the loser
	/// attached, which matches the survivor in every other field.
	/// </summary>
	async ValueTask<Contracts.StoredMemory?> FindIdenticalAsync(Contracts.Memory memory, CancellationToken ct) {
		if (await store.FindLiveByContentAsync(memory.Content, ct).ConfigureAwait(false) is not { } existing)
			return null;

		var tags = existing.Tags.Select(KontextMemoryDataStore.EncodeTag).ToHashSet();

		return tags.SetEquals(memory.Tags.Select(KontextMemoryDataStore.EncodeTag))
		    && existing.Evidence.ToHashSet().SetEquals(memory.Evidence)
		    && existing.Supersedes.ToHashSet().SetEquals(memory.Supersedes)
			? existing
			: null;
	}

	/// <summary>
	/// Rejects a retain pointing at a memory the store cannot resolve. Both id-carrying fields are
	/// checked, under DIFFERENT rules: <c>supersedes</c> must name a LIVE TIP, while a
	/// <c>MemoryRef</c> citation need only EXIST — evidence is frozen at retain, so citing a memory
	/// superseded later records what the claim actually rested on.
	/// </summary>
	async ValueTask EnsureReferencedMemoriesResolveAsync(Contracts.MemoriesRetained retained, CancellationToken ct) {
		var superseded = retained.Memories.SelectMany(entry => entry.Memory.Supersedes).ToHashSet();
		var cited      = retained.Memories.SelectMany(entry => KontextMemoryDataStore.EncodeCitedMemoryIds(entry.Memory)).ToHashSet();

		if (superseded.Count == 0 && cited.Count == 0)
			return;

		var tips = await store.GetSupersessionStatusAsync([..superseded, ..cited], ct).ConfigureAwait(false);

		var failures = new List<ValidationFailure>();

		if (superseded.Where(id => !tips.ContainsKey(id)).ToList() is { Count: > 0 } missingTargets)
			failures.Add(new ValidationFailure(
				nameof(Contracts.Memory.Supersedes),
				$"No such memory: {string.Join(", ", missingTargets)}."
			));

		if (superseded.Where(id => tips.TryGetValue(id, out var tip) && !tip.IsLive).ToList() is { Count: > 0 } staleTargets)
			failures.Add(new ValidationFailure(
				nameof(Contracts.Memory.Supersedes),
				$"Already superseded — supersede the live tip instead: {string.Join(", ", staleTargets.Select(id => $"{id} -> {tips[id].SupersededBy}"))}."
			));

		if (cited.Where(id => !tips.ContainsKey(id)).ToList() is { Count: > 0 } missingCitations)
			failures.Add(new ValidationFailure(
				nameof(Contracts.Memory.Evidence),
				$"A memory citation names no such memory: {string.Join(", ", missingCitations)}."
			));

		if (failures.Count > 0)
			throw new RequestValidationException(failures);
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
		validation.Validate(request);

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

		await RecordRecallAsync(request, response.QueryId, ranked, ct).ConfigureAwait(false);

		return response;
	}

	/// <summary>
	/// Appends the recall that just happened, which is what advances every returned memory's recency
	/// clock — retrieval IS an access, so without this event recency could only ever fall and the
	/// store would decay precisely what it uses most.
	/// <para>A failure here fails the recall, deliberately. Kontext runs inside the database it appends
	/// to, so an append that cannot land means the node is already broken — there is no degraded mode
	/// to preserve, and a recall that silently stopped advancing the clock would hide that.</para>
	/// </summary>
	async ValueTask RecordRecallAsync(
		Contracts.RecallRequest request, string queryId, IReadOnlyList<Retrieval.ScoredMemory> ranked, CancellationToken ct
	) {
		// A recall that matched nothing accessed nothing.
		if (ranked.Count == 0)
			return;

		var recalled = new Contracts.MemoriesRecalled {
			QueryId    = queryId,
			Query      = request.Query,
			Limit      = request.Limit,
			MinScore   = request.MinScore,
			Tags       = { request.Tags },
			RecalledAt = Timestamp.FromDateTimeOffset(time.GetUtcNow()),
		};

		// `config` is left unset: the scoring weights live inside the retrieval pipeline and it does
		// not surface them. The decay mechanism does not need them — only replaying a past ranking
		// would, and nothing does that yet.
		foreach (var scored in ranked)
			recalled.Memories.Add(new Contracts.ScoredMemory {
				MemoryId       = scored.Memory.MemoryId,
				LastAccessedAt = scored.Memory.LastAccessedAt,
				Score          = scored.Score,
				// Nullable on the retrieval side: a stage that never ran contributes nothing rather
				// than a fabricated number, and lands here as the proto's default.
				RecencyRaw     = scored.Breakdown.RecencyRaw     ?? 0,
				ImportanceRaw  = scored.Breakdown.ImportanceRaw  ?? 0,
				RelevanceRaw   = scored.Breakdown.RelevanceRaw   ?? 0,
				RecencyNorm    = scored.Breakdown.RecencyNorm    ?? 0,
				ImportanceNorm = scored.Breakdown.ImportanceNorm ?? 0,
				RelevanceNorm  = scored.Breakdown.RelevanceNorm  ?? 0,
			});

		await appendEvent(recalled, ct).ConfigureAwait(false);
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

	// Not async iterators: an invalid request must be rejected on the call, not on first enumeration.
	public IAsyncEnumerable<Contracts.StoredMemory> ReclaimAsync(Contracts.ReclaimRequest request, CancellationToken ct = default) {
		validation.Validate(request);

		return store.GetAsync([.. request.Ids], ct);
	}

	public IAsyncEnumerable<Contracts.StoredMemory> RecollectAsync(Contracts.RecollectRequest request, CancellationToken ct = default) {
		validation.Validate(request);

		var top = request.Limit > 0 ? request.Limit : DefaultRecollectLimit;

		return store.ListAsync(request.Tags, request.Types_, request.Sort, request.Direction, top, ct);
	}

	/// <summary>
	/// Records that the named memories were actually used, advancing their recency clocks.
	/// <para>All-or-nothing, and every id must name a LIVE TIP. Missing is a caller bug — these ids came
	/// from a recall or a reclaim. Superseded is worse than a bug: recall never surfaces a superseded
	/// memory, so its recency clock feeds no ranking, and accepting one would report success for a write
	/// nothing can ever read. It also means the caller acted on a claim that has since been corrected,
	/// which is the one thing worth telling it — so the rejection hands back the tip, exactly as retain
	/// does for a stale supersession target.</para>
	/// </summary>
	public async ValueTask<Contracts.ReinforceResponse> ReinforceAsync(Contracts.ReinforceRequest request, CancellationToken ct = default) {
		validation.Validate(request);

		var ids   = request.Ids.ToHashSet();
		var known = await store.GetSupersessionStatusAsync(ids, ct).ConfigureAwait(false);

		var failures = new List<ValidationFailure>();

		if (ids.Where(id => !known.ContainsKey(id)).ToList() is { Count: > 0 } missing)
			failures.Add(new ValidationFailure(
				nameof(Contracts.ReinforceRequest.Ids),
				$"No such memory: {string.Join(", ", missing)}."
			));

		if (ids.Where(id => known.TryGetValue(id, out var tip) && !tip.IsLive).ToList() is { Count: > 0 } superseded)
			failures.Add(new ValidationFailure(
				nameof(Contracts.ReinforceRequest.Ids),
				$"Superseded — you acted on a memory that has been corrected; reinforce the tip instead: {string.Join(", ", superseded.Select(id => $"{id} -> {known[id].SupersededBy}"))}."
			));

		if (failures.Count > 0)
			throw new RequestValidationException(failures);

		var accessedAt = time.GetUtcNow();

		// ONE event for the whole call, like retain: the batch is the unit of the operation, and the
		// projector folds a batch to one row state per id regardless.
		var reinforced = new Contracts.MemoriesReinforced { ReinforcedAt = Timestamp.FromDateTimeOffset(accessedAt) };
		reinforced.MemoryIds.AddRange(ids);

		await appendEvent(reinforced, ct).ConfigureAwait(false);

		return new() { AccessedAt = Timestamp.FromDateTimeOffset(accessedAt) };
	}
}
