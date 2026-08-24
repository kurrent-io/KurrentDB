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
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

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
	/// Decides each memory against what is already stored, then appends ONE event carrying only
	/// what it actually wrote. A written memory becomes recallable when the projector applies that
	/// event, so this stays eventually consistent: retain-then-recall in the same breath finds
	/// nothing.
	/// </summary>
	public async ValueTask<MemoryContracts.RetainResponse> RetainAsync(MemoryContracts.RetainRequest request, CancellationToken ct = default) {
		var response = new MemoryContracts.RetainResponse();

		var retained = new MemoryContracts.MemoriesRetained {
			RetainedAt = Timestamp.FromDateTimeOffset(time.GetUtcNow()),
		};

		var decisions = await DecideAsync(request, ct).ConfigureAwait(false);

		// Results are appended in lockstep with the request because the contract promises
		// results[i] is the memory sent at memories[i].
		for (var i = 0; i < request.Memories.Count; i++) {
			var memory   = request.Memories[i];
			var decision = decisions[i];
			var result   = new MemoryContracts.RetainResponse.Types.RetainResult { Outcome = decision.Outcome };

			switch (decision.Outcome) {
				case MemoryContracts.RetainOutcome.Noop:
					result.MemoryId = decision.Existing!.MemoryId;
					break;

				case MemoryContracts.RetainOutcome.Deferred:
					result.Candidates.AddRange(decision.Candidates);
					break;

				default:
					// The server mints every id — a caller can neither collide with an existing
					// memory nor cite one it is sending in this same batch.
					var memoryId = Guid.CreateVersion7().ToString();

					if (decision.Existing is not null)
						Absorb(memory, decision.Existing);

					retained.Memories.Add(new MemoryContracts.MemoriesRetained.Types.RetainedMemory { MemoryId = memoryId, Memory = memory });

					result.MemoryId = memoryId;
					result.SupersededMemoryIds.AddRange(memory.Supersedes);
					break;
			}

			response.Results.Add(result);
		}

		// ONE event per call, never one per memory: the batch is the unit of the operation, so a
		// reader can never observe half of it and the projector pays one commit instead of N. A
		// batch that only NOOPed or DEFERRED wrote nothing, and an event for it would carry nothing.
		if (retained.Memories.Count > 0)
			await appendEvent(retained, ct).ConfigureAwait(false);

		return response;

		// Folds the memory being superseded into its successor: the id, the tags, the citations.
		static void Absorb(MemoryContracts.Memory memory, MemoryContracts.StoredMemory existing) {
			memory.Supersedes.Add(existing.MemoryId);

			var tags = memory.Tags.Select(KontextMemoryDataStore.EncodeTag).ToHashSet();
			memory.Tags.AddRange(existing.Tags.Where(tag => tags.Add(KontextMemoryDataStore.EncodeTag(tag))));

			// Support accumulates along a supersession chain: the successor carries its own
			// citations plus the ones it replaces, so nothing already checked is lost.
			var evidence = memory.Evidence.ToHashSet();
			memory.Evidence.AddRange(existing.Evidence.Where(evidence.Add));
		}
	}

	/// <summary>What retain will do with one incoming memory, and the stored memory it turns on.</summary>
	readonly record struct RetainDecision(
		MemoryContracts.RetainOutcome Outcome,
		MemoryContracts.StoredMemory? Existing,
		IReadOnlyList<MemoryContracts.RetainResponse.Types.RelatedMemory> Candidates
	);

	/// <summary>Resolves every incoming memory to an outcome, cheapest test first.</summary>
	async ValueTask<RetainDecision[]> DecideAsync(MemoryContracts.RetainRequest request, CancellationToken ct) {
		var decisions = new RetainDecision[request.Memories.Count];
		var undecided = new List<int>();

		for (var i = 0; i < request.Memories.Count; i++) {
			// An explicit `supersedes` IS the caller's decision, and `decided` says they already
			// read the candidates. Re-checking either would second-guess a call that was theirs.
			if (request.Memories[i].Supersedes.Count > 0)
				decisions[i] = new(MemoryContracts.RetainOutcome.Merged, null, []);
			else if (request.Decided)
				decisions[i] = new(MemoryContracts.RetainOutcome.Created, null, []);
			else
				undecided.Add(i);
		}

		var inferential = new List<int>();

		// Exact content first: deterministic, and it needs no embedding, so a byte-identical memory
		// never reaches the inferential path at all.
		foreach (var i in undecided) {
			var memory   = request.Memories[i];
			var existing = await store.FindLiveByContentAsync(memory.Content, ct).ConfigureAwait(false);

			if (existing is null) {
				inferential.Add(i);
				continue;
			}

			// The same claim under tags the stored memory already carries adds nothing. New tags
			// widen its reach, and widening is a merge.
			decisions[i] = Covers(existing.Tags, memory.Tags)
				? new(MemoryContracts.RetainOutcome.Noop, existing, [])
				: new(MemoryContracts.RetainOutcome.Merged, existing, []);
		}

		if (inferential.Count == 0)
			return decisions;

		// One embedding call for the batch: the per-item cost is the same, and it keeps model round
		// trips to one.
		var contents   = inferential.Select(i => request.Memories[i].Content).ToArray();
		var embedded   = await embeddings.GenerateAsync(contents, EmbeddingOptions, ct).ConfigureAwait(false);
		var searchOpts = new HybridSearchOptions { K = options.RelatedLimit, Alpha = options.RelatedAlpha };

		for (var n = 0; n < inferential.Count; n++) {
			var i      = inferential[n];
			var memory = request.Memories[i];

			var hits = new List<MemoryHit>();

			// Scoped by the memory's own tags. Once the server stamps `user`, that is what keeps the
			// search from crossing principals; an untagged memory is searched unscoped.
			await foreach (var hit in store.SearchAsync(memory.Content, embedded[n].Vector.ToArray(), memory.Tags, searchOpts, ct).ConfigureAwait(false))
				hits.Add(hit);

			// Only the vector leg yields a distance that means the same thing in every query: it is
			// raw squared L2 over unit-length embeddings. The engine's blended score is min-max
			// normalised across whatever that one search returned and adds nothing for a leg that
			// missed the row, so a keyword-only hit informs the caller but never the branch.
			var placed = hits.Where(hit => hit.VectorDistance is not null).ToArray();

			if (placed.Length == 0) {
				decisions[i] = new(MemoryContracts.RetainOutcome.Created, null, []);
				continue;
			}

			var nearest  = placed.MinBy(hit => hit.VectorDistance!.Value);
			var distance = nearest.VectorDistance!.Value;

			decisions[i] = distance switch {
				_ when distance < options.MergeCeiling => new(MemoryContracts.RetainOutcome.Merged, nearest.Memory, []),
				_ when distance > options.AppendFloor  => new(MemoryContracts.RetainOutcome.Created, null, []),
				_                                      => new(MemoryContracts.RetainOutcome.Deferred, null, [.. hits.Select(ToRelated)]),
			};
		}

		return decisions;

		// True when every incoming tag is already on the stored memory.
		static bool Covers(IEnumerable<MemoryContracts.Tag> stored, IEnumerable<MemoryContracts.Tag> incoming) {
			var present = stored.Select(KontextMemoryDataStore.EncodeTag).ToHashSet();

			return incoming.All(tag => present.Contains(KontextMemoryDataStore.EncodeTag(tag)));
		}

		static MemoryContracts.RetainResponse.Types.RelatedMemory ToRelated(MemoryHit hit) =>
			new() {
				// Infinity when the vector leg never placed this row: the keyword leg alone found
				// it, and reporting 0 would read as identical.
				Distance     = hit.VectorDistance ?? double.PositiveInfinity,
				KeywordMatch = hit.KeywordScore is not null,
				Memory       = ToLean(hit.Memory),
			};
	}

	public async ValueTask<MemoryContracts.RecallResponse> RecallAsync(MemoryContracts.RecallRequest request, CancellationToken ct = default) {
		var response = new MemoryContracts.RecallResponse {
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
			var memory = new MemoryContracts.RecallResponse.Types.RecalledMemory { Score = scored.Score };

			if (request.IncludeFull)
				memory.Full = scored.Memory;
			else
				memory.Lean = ToLean(scored.Memory);

			response.Memories.Add(memory);
		}

		return response;
	}

	/// <summary>The lean projection both recall hits and retain's candidates are reported as.</summary>
	static MemoryContracts.LeanMemory ToLean(MemoryContracts.StoredMemory stored) {
		var lean = new MemoryContracts.LeanMemory {
			MemoryId   = stored.MemoryId,
			MemoryType = stored.MemoryType,
			Content    = stored.Content,
			Importance = stored.Importance,
			RetainedAt = stored.RetainedAt,
		};

		lean.Tags.AddRange(stored.Tags);
		return lean;
	}

	public IAsyncEnumerable<MemoryContracts.StoredMemory> ReclaimAsync(MemoryContracts.ReclaimRequest request, CancellationToken ct = default) =>
		store.GetAsync([.. request.Ids], ct);

	public async IAsyncEnumerable<MemoryContracts.StoredMemory> RecollectAsync(MemoryContracts.RecollectRequest request, [EnumeratorCancellation] CancellationToken ct = default) {
		var top = request.Limit > 0 ? request.Limit : DefaultRecollectLimit;

		await foreach (var memory in store.ListAsync(request.Tags, request.Types_, request.Sort, request.Direction, top, ct).ConfigureAwait(false))
			yield return memory;
	}

	public ValueTask<MemoryContracts.ReflectResponse> ReflectAsync(MemoryContracts.ReflectRequest request, CancellationToken ct = default) =>
		throw new NotImplementedException("Reflect synthesizes derived memories with a language model — not part of the data-store surface.");
}
