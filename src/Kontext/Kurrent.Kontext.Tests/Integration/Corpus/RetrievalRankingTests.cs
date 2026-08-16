// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Retrieval;
using Serilog;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Scores the whole ranking against ground truth instead of asserting on one named row — the only
/// suite that fails on a composition which still returns the right memory but ranks it worse.
/// </summary>
// The corpus session (419 sequential ONNX embeds) initializes inside the first test's clock, and every
// evaluation embeds 150 questions per composition — the assembly-wide 60s timeout cannot hold these.
[Category("Benchmark")]
[Timeout(300_000)]
public class RetrievalRankingTests {
	// Measured on 419 memories / 150 questions with pMM12, pinned asOf, Limit 10: the default
	// composition scored recall@5 0.4700, mrr 0.3832, ndcg@10 0.4120. Floors sit under those so only
	// a regression trips them; raise one when a change moves the measurement up for a named reason.
	// Raised from 0.33/0.23/0.28 when the pool-local BM25 reread (Bm25Reranker) joined the default
	// composition.
	const double RecallAt5Floor = 0.43;
	const double MrrFloor       = 0.34;
	const double NdcgAt10Floor  = 0.37;

	[ClassDataSource<KontextCorpus>(Shared = SharedType.PerTestSession)]
	public required KontextCorpus Corpus { get; init; }

	[Test]
	public async ValueTask corpus_seeds_every_turn_with_resolvable_ground_truth(CancellationToken ct) {
		// Truncation would inflate every metric below by removing distractors.
		await Assert.That(Corpus.MemoryCount).IsEqualTo(419);
		await Assert.That(Corpus.Questions.Count).IsEqualTo(150);
		await Assert.That(Corpus.Questions.All(question => question.Relevant.Count > 0)).IsTrue();
	}

	[Test]
	public async ValueTask default_pipeline_meets_the_ranking_floor(CancellationToken ct) {
		// Act
		var outcomes = await Corpus.Evaluate(DefaultPipeline(), ct: ct);

		// Assert
		Report("default", outcomes);

		await Assert.That(RankingMetrics.RecallAt(outcomes, 5)).IsGreaterThanOrEqualTo(RecallAt5Floor);
		await Assert.That(RankingMetrics.Mrr(outcomes)).IsGreaterThanOrEqualTo(MrrFloor);
		await Assert.That(RankingMetrics.NdcgAt(outcomes, 10)).IsGreaterThanOrEqualTo(NdcgAt10Floor);
	}

	[Test]
	public async ValueTask fusion_beats_both_single_legs(CancellationToken ct) {
		// Arrange + Act
		var both    = await Corpus.Evaluate(DefaultPipeline(), ct: ct);
		var vector  = await Corpus.Evaluate(SingleLegPipeline(vectorLeg: true), ct: ct);
		var keyword = await Corpus.Evaluate(SingleLegPipeline(vectorLeg: false), ct: ct);

		Report("both legs", both);
		Report("vector only", vector);
		Report("keyword only", keyword);

		// Assert
		var fused = RankingMetrics.NdcgAt(both, 10);

		await Assert.That(fused).IsGreaterThan(RankingMetrics.NdcgAt(vector, 10));
		await Assert.That(fused).IsGreaterThan(RankingMetrics.NdcgAt(keyword, 10));
	}

	[Test]
	public async ValueTask focused_beats_the_shipped_hybrid(CancellationToken ct) {
		// The 2026-08-15 hill-climb winner: alpha 0.45, no MMR. Both chains are single-leg and
		// measure deterministically, so the comparison holds without a tolerance band.
		var focused = await Corpus.Evaluate(FocusedPipeline(), ct: ct);
		var hybrid  = await Corpus.Evaluate(HybridPipeline(), ct: ct);

		Report("focused", focused);
		Report("hybrid", hybrid);

		// Assert
		await Assert.That(RankingMetrics.RecallAt(focused, 5))
			.IsGreaterThanOrEqualTo(RankingMetrics.RecallAt(hybrid, 5));
		await Assert.That(RankingMetrics.NdcgAt(focused, 10))
			.IsGreaterThanOrEqualTo(RankingMetrics.NdcgAt(hybrid, 10));

		IKontextRetriever FocusedPipeline() =>
			KontextRetriever.New().Focused(Corpus.Store, Corpus.EmbeddingGenerator).Build();

		IKontextRetriever HybridPipeline() =>
			KontextRetriever.New().Hybrid(Corpus.Store, Corpus.EmbeddingGenerator).Build();
	}

	[Test]
	public async ValueTask shipped_hybrid_beats_the_legacy_baseline(CancellationToken ct) {
		// Production wires the Hybrid chain (CreateDefaultRetriever), not Default — this test is
		// the three-way comparison the composition decision reads.
		var hybrid  = await Corpus.Evaluate(HybridPipeline(), ct: ct);
		var current = await Corpus.Evaluate(DefaultPipeline(), ct: ct);
		var legacy  = await Corpus.Evaluate(LegacyPipeline(), ct: ct);

		Report("hybrid", hybrid);
		Report("default", current);
		Report("legacy", legacy);

		// Assert
		await Assert.That(RankingMetrics.NdcgAt(hybrid, 10))
			.IsGreaterThan(RankingMetrics.NdcgAt(legacy, 10));

		IKontextRetriever HybridPipeline() =>
			KontextRetriever.New().Hybrid(Corpus.Store, Corpus.EmbeddingGenerator).Build();
	}

	[Test]
	public async ValueTask default_pipeline_beats_the_legacy_baseline(CancellationToken ct) {
		// Arrange + Act
		var current = await Corpus.Evaluate(DefaultPipeline(), ct: ct);
		var legacy  = await Corpus.Evaluate(LegacyPipeline(), ct: ct);

		Report("default", current);
		Report("legacy", legacy);

		// Assert
		await Assert.That(RankingMetrics.NdcgAt(current, 10))
			.IsGreaterThanOrEqualTo(RankingMetrics.NdcgAt(legacy, 10));
	}

	#region ->> Test Infrastructure <<-

	// A failing floor has to show what the run measured, or there is nothing to recalibrate from.
	static void Report(string composition, IReadOnlyList<RankedOutcome> outcomes) =>
		Log.Information("{Composition}: recall@5 {Recall:F4}, mrr {Mrr:F4}, ndcg@10 {Ndcg:F4}",
			composition,
			RankingMetrics.RecallAt(outcomes, 5),
			RankingMetrics.Mrr(outcomes),
			RankingMetrics.NdcgAt(outcomes, 10));

	IKontextRetriever DefaultPipeline() =>
		KontextRetriever.New().Default(Corpus.Store, Corpus.EmbeddingGenerator).Build();

	IKontextRetriever LegacyPipeline() =>
		KontextRetriever.New().Legacy(Corpus.Store, Corpus.EmbeddingGenerator).Build();

	/// <summary>One leg, same stages — the ablation the fusion is measured against.</summary>
	IKontextRetriever SingleLegPipeline(bool vectorLeg) =>
		KontextRetriever.New()
			.AddSearch(vectorLeg
				? new VectorSearch(Corpus.Store, Corpus.EmbeddingGenerator)
				: new KeywordSearch(Corpus.Store))
			.AddStage(Bm25Reranker.Create())
			.AddStage(CognitiveModulator.Create())
			.AddStage(MmrReorderer.Create())
			.Build();

	#endregion // Test Infrastructure
}
