// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests;

[Category("Scoring")]
public class ScoreNormalizationTests {
	[Test]
	[Arguments(0.0, 1.0)]
	[Arguments(1.0, 0.5)]
	[Arguments(3.0, 0.25)]
	[Arguments(7.0, 0.125)]
	[Arguments(15.0, 0.0625)]
	public async ValueTask relevance_from_distance_matches_inverse_formula(double distance, double expected) {
		await Assert.That(ScoreNormalization.RelevanceFromDistance(distance)).IsEqualTo(expected);
	}

	[Test]
	public async ValueTask relevance_from_distance_decreases_monotonically_as_distance_grows() {
		double[] distances = [0, 1, 2, 5, 100, 100_000];
		var relevances = distances.Select(ScoreNormalization.RelevanceFromDistance).ToList();

		for (var i = 1; i < relevances.Count; i++)
			await Assert.That(relevances[i]).IsLessThan(relevances[i - 1]);
	}

	[Test]
	public async ValueTask relevance_from_distance_clamps_negative_distance_before_dividing() {
		// Math.Max(0, distance) means any negative distance behaves exactly like distance 0.
		await Assert.That(ScoreNormalization.RelevanceFromDistance(-5)).IsEqualTo(1.0);
		await Assert.That(ScoreNormalization.RelevanceFromDistance(-1_000)).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask relevance_from_distance_approaches_zero_without_going_negative() {
		var relevance = ScoreNormalization.RelevanceFromDistance(1_000_000_000);

		await Assert.That(relevance).IsGreaterThan(0.0);
		await Assert.That(relevance).IsLessThan(0.0001);
	}

	[Test]
	public async ValueTask sigmoid_at_midpoint_is_exactly_half() {
		await Assert.That(ScoreNormalization.Sigmoid(5, 5, 1)).IsEqualTo(0.5);
		await Assert.That(ScoreNormalization.Sigmoid(-3, -3, 4)).IsEqualTo(0.5);
	}

	[Test]
	public async ValueTask sigmoid_saturates_far_from_midpoint() {
		await Assert.That(ScoreNormalization.Sigmoid(15, 5, 1)).IsGreaterThan(0.99);
		await Assert.That(ScoreNormalization.Sigmoid(-5, 5, 1)).IsLessThan(0.05);
	}

	[Test]
	public async ValueTask sigmoid_matches_derived_logistic_expression() {
		// value 2 raw units below midpoint at steepness 1: 1 / (1 + e^(-1 * (-1 - 1))) = 1 / (1 + e^2)
		await Assert.That(ScoreNormalization.Sigmoid(-1, 1, 1)).IsEqualTo(1.0 / (1.0 + Math.Exp(2))).Within(1e-12);
	}

	[Test]
	[Arguments(-1_000.0)]
	[Arguments(-10.0)]
	[Arguments(0.0)]
	[Arguments(10.0)]
	[Arguments(1_000.0)]
	public async ValueTask sigmoid_stays_within_unit_interval_across_a_wide_sweep(double value) {
		var result = ScoreNormalization.Sigmoid(value, 0, 0.5);

		await Assert.That(result).IsGreaterThanOrEqualTo(0.0);
		await Assert.That(result).IsLessThanOrEqualTo(1.0);
	}

	[Test]
	public async ValueTask higher_steepness_sharpens_the_transition() {
		var gentle = ScoreNormalization.Sigmoid(1, 0, 1);
		var steep  = ScoreNormalization.Sigmoid(1, 0, 10);

		await Assert.That(gentle).IsGreaterThan(0.5);
		await Assert.That(steep).IsGreaterThan(gentle);
	}

	[Test]
	public async ValueTask minmax_maps_pool_bounds_to_unit_interval() {
		await Assert.That(ScoreNormalization.MinMax(1, 1, 10)).IsEqualTo(0.0);
		await Assert.That(ScoreNormalization.MinMax(10, 1, 10)).IsEqualTo(1.0);
		await Assert.That(ScoreNormalization.MinMax(5.5, 1, 10)).IsEqualTo(4.5 / 9).Within(1e-12);
	}

	[Test]
	public async ValueTask minmax_degenerate_pool_returns_neutral_half_not_zero_or_one() {
		// max == min has nothing to discriminate on; this is a deliberate choice, not an edge-case leak.
		await Assert.That(ScoreNormalization.MinMax(5, 5, 5)).IsEqualTo(0.5);
		await Assert.That(ScoreNormalization.MinMax(0, 0, 0)).IsEqualTo(0.5);
	}

	[Test]
	public async ValueTask minmax_clamps_out_of_range_values() {
		await Assert.That(ScoreNormalization.MinMax(-5, 1, 10)).IsEqualTo(0.0);
		await Assert.That(ScoreNormalization.MinMax(20, 1, 10)).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask exponential_decay_at_age_zero_is_full_freshness() {
		await Assert.That(ScoreNormalization.ExponentialDecay(TimeSpan.Zero, TimeSpan.FromHours(1))).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask exponential_decay_matches_e_to_the_negative_age_over_tau() {
		var tau = TimeSpan.FromHours(6);

		await Assert.That(ScoreNormalization.ExponentialDecay(tau, tau)).IsEqualTo(Math.Exp(-1)).Within(1e-12);
		await Assert.That(ScoreNormalization.ExponentialDecay(tau * 3, tau)).IsEqualTo(Math.Exp(-3)).Within(1e-12);
	}

	[Test]
	public async ValueTask exponential_decay_future_dated_age_is_never_penalized() {
		await Assert.That(ScoreNormalization.ExponentialDecay(TimeSpan.FromHours(-5), TimeSpan.FromHours(1))).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask exponential_decay_decreases_monotonically_with_age() {
		var tau = TimeSpan.FromHours(1);
		TimeSpan[] ages = [TimeSpan.Zero, tau, tau * 2, tau * 5, tau * 20];
		var decays = ages.Select(age => ScoreNormalization.ExponentialDecay(age, tau)).ToList();

		for (var i = 1; i < decays.Count; i++)
			await Assert.That(decays[i]).IsLessThan(decays[i - 1]);
	}

	[Test]
	public async ValueTask halflife_decay_at_age_zero_is_full_freshness() {
		await Assert.That(ScoreNormalization.HalfLifeDecay(TimeSpan.Zero, TimeSpan.FromDays(1))).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask halflife_decay_halves_exactly_at_each_half_life() {
		var halfLife = TimeSpan.FromDays(3);

		await Assert.That(ScoreNormalization.HalfLifeDecay(halfLife, halfLife)).IsEqualTo(0.5);
		await Assert.That(ScoreNormalization.HalfLifeDecay(halfLife * 2, halfLife)).IsEqualTo(0.25);
	}

	[Test]
	public async ValueTask halflife_decay_future_dated_age_is_never_penalized() {
		await Assert.That(ScoreNormalization.HalfLifeDecay(TimeSpan.FromDays(-2), TimeSpan.FromDays(1))).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask jaccard_identical_and_disjoint_boundaries() {
		await Assert.That(ScoreNormalization.JaccardSimilarity("the quick brown fox", "the quick brown fox")).IsEqualTo(1.0);
		await Assert.That(ScoreNormalization.JaccardSimilarity("apples oranges", "bicycles rockets")).IsEqualTo(0.0);
	}

	[Test]
	public async ValueTask jaccard_hand_computed_partial_overlap() {
		// left tokens = {quick, brown, fox, jumps}, right tokens = {quick, brown, lazy, dog}
		// intersection = {quick, brown} = 2; union = 4 + 4 - 2 = 6; jaccard = 2/6 = 1/3
		var jaccard = ScoreNormalization.JaccardSimilarity("quick brown fox jumps", "quick brown lazy dog");

		await Assert.That(jaccard).IsEqualTo(2.0 / 6).Within(1e-12);
	}

	[Test]
	public async ValueTask jaccard_empty_or_whitespace_only_input_scores_zero() {
		await Assert.That(ScoreNormalization.JaccardSimilarity("", "something meaningful")).IsEqualTo(0.0);
		await Assert.That(ScoreNormalization.JaccardSimilarity("   \t\n", "hello world")).IsEqualTo(0.0);
		await Assert.That(ScoreNormalization.JaccardSimilarity("", "")).IsEqualTo(0.0);
	}

	[Test]
	public async ValueTask jaccard_matching_is_case_insensitive() {
		// Pins the intended contract: Tokenize builds each side with StringComparer.OrdinalIgnoreCase,
		// so casing alone must not affect the score. KNOWN BUG (not fixed here, see test project report):
		// `a.Intersect(b)` is Enumerable.Intersect with no comparer argument, which always rebuilds a
		// fresh HashSet from `b` using the *default* (case-sensitive) comparer — Tokenize's ignore-case
		// comparer never reaches the cross-set comparison. This assertion currently fails at runtime.
		await Assert.That(ScoreNormalization.JaccardSimilarity("Quick Brown", "quick brown")).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask jaccard_drops_single_character_tokens() {
		// every token in both strings has length 1, so Tokenize's `token.Length > 1` filter empties
		// both sets and the a.Count == 0 short-circuit fires, even though the raw text "overlaps".
		await Assert.That(ScoreNormalization.JaccardSimilarity("a b c d", "a b c d")).IsEqualTo(0.0);
	}

	[Test]
	public async ValueTask jaccard_splits_on_punctuation_and_other_separators() {
		// "hello,world;foo-bar" tokenizes to the same {hello, world, foo, bar} set as space-separated text
		var jaccard = ScoreNormalization.JaccardSimilarity("hello,world;foo-bar", "hello world foo bar");

		await Assert.That(jaccard).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask jaccard_is_symmetric() {
		var left  = "quick brown fox jumps";
		var right = "quick brown lazy dog";

		await Assert.That(ScoreNormalization.JaccardSimilarity(left, right))
			.IsEqualTo(ScoreNormalization.JaccardSimilarity(right, left));
	}
}
