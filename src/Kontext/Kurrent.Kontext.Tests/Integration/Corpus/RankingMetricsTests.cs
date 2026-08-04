// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Tests;

/// <summary>Hand-computed expectations for the metrics the corpus tier's floors are expressed in.</summary>
public class RankingMetricsTests {
	const double Tolerance = 1e-9;

	[Test]
	[Arguments("a b c d", "b d", 3, 0.5)]
	[Arguments("a b c d", "b d", 4, 1.0)]
	[Arguments("x y z", "a", 5, 0.0)]
	public async ValueTask recall_counts_the_relevant_hits_inside_the_cut(string returned, string relevant, int k, double expected) =>
		await Assert.That(RankingMetrics.RecallAt([Outcome(returned, relevant)], k)).IsEqualTo(expected).Within(Tolerance);

	[Test]
	public async ValueTask recall_is_macro_averaged_so_every_question_weighs_the_same() {
		// One perfect question with 1 relevant, one that finds 1 of 4: macro is (1.0 + 0.25) / 2,
		// where a micro average would give 2/5 = 0.4.
		RankedOutcome[] outcomes = [Outcome("a", "a"), Outcome("b x y z", "b c d e")];

		await Assert.That(RankingMetrics.RecallAt(outcomes, 10)).IsEqualTo(0.625).Within(Tolerance);
	}

	[Test]
	[Arguments("a b", "a b", 1.0)]
	[Arguments("x y a b", "a b", 1.0 / 3.0)]
	[Arguments("x y z", "a", 0.0)]
	public async ValueTask mrr_uses_the_first_relevant_rank_only(string returned, string relevant, double expected) =>
		await Assert.That(RankingMetrics.Mrr([Outcome(returned, relevant)])).IsEqualTo(expected).Within(Tolerance);

	[Test]
	[Arguments("a b c", "a b", 3, 1.0)]
	[Arguments("a b c", "a b c", 1, 1.0)]  // 3 relevant but k=1: one hit at rank 1 is all that fits
	public async ValueTask ndcg_is_one_for_the_best_ranking_the_cut_allows(string returned, string relevant, int k, double expected) =>
		await Assert.That(RankingMetrics.NdcgAt([Outcome(returned, relevant)], k)).IsEqualTo(expected).Within(Tolerance);

	[Test]
	public async ValueTask ndcg_discounts_by_position() {
		// One relevant memory at rank 2: DCG = 1/log2(3), IDCG = 1/log2(2) = 1.
		await Assert.That(RankingMetrics.NdcgAt([Outcome("x a", "a")], 2)).IsEqualTo(1.0 / Math.Log2(3)).Within(Tolerance);
	}

	[Test]
	public async ValueTask questions_without_ground_truth_are_skipped_not_scored_as_zero() {
		RankedOutcome[] outcomes = [Outcome("a", "a"), Outcome("x y", "")];

		await Assert.That(RankingMetrics.RecallAt(outcomes, 5)).IsEqualTo(1.0).Within(Tolerance);
		await Assert.That(RankingMetrics.Mrr(outcomes)).IsEqualTo(1.0).Within(Tolerance);
		await Assert.That(RankingMetrics.NdcgAt(outcomes, 5)).IsEqualTo(1.0).Within(Tolerance);
	}

	[Test]
	public async ValueTask empty_input_scores_zero_rather_than_throwing() {
		await Assert.That(RankingMetrics.RecallAt([], 5)).IsEqualTo(0.0).Within(Tolerance);
		await Assert.That(RankingMetrics.Mrr([])).IsEqualTo(0.0).Within(Tolerance);
		await Assert.That(RankingMetrics.NdcgAt([], 5)).IsEqualTo(0.0).Within(Tolerance);
	}

	static RankedOutcome Outcome(string returned, string relevant) =>
		new(returned.Split(' ', StringSplitOptions.RemoveEmptyEntries),
			relevant.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet());
}
