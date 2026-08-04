// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Testing;

/// <summary>One question's outcome: the ids returned, best first, against the ids the corpus calls relevant.</summary>
public sealed record RankedOutcome(IReadOnlyList<string> Returned, IReadOnlySet<string> Relevant);

/// <summary>
/// Standard information-retrieval metrics.
/// <para>- all scores in [0,1], higher = better</para>
/// <para>- relevance is binary: a returned id is in the ground-truth set or it is not</para>
/// <para>- list-level overloads are macro averages: every question weighs the same, 1 relevant memory or 6</para>
/// </summary>
public static class RankingMetrics {
	/// <summary>Mean <see cref="RecallAt(RankedOutcome, int)"/> across questions.</summary>
	public static double RecallAt(IEnumerable<RankedOutcome> outcomes, int k) =>
		Mean(outcomes, outcome => RecallAt(outcome, k));

	/// <summary>Mean <see cref="ReciprocalRank"/> across questions.</summary>
	public static double Mrr(IEnumerable<RankedOutcome> outcomes) =>
		Mean(outcomes, ReciprocalRank);

	/// <summary>Mean <see cref="NdcgAt(RankedOutcome, int)"/> across questions.</summary>
	public static double NdcgAt(IEnumerable<RankedOutcome> outcomes, int k) =>
		Mean(outcomes, outcome => NdcgAt(outcome, k));

	#region ->> Per-question scores <<-

	/// <summary>
	/// Of everything that should be found, how much made the top <paramref name="k"/>?
	/// <para>1.0 = all relevant memories in the top k, 0 = none.</para>
	/// <para>Position inside the top k doesn't matter: rank 1 and rank k count the same.</para>
	/// </summary>
	public static double RecallAt(RankedOutcome outcome, int k) =>
		(double)outcome.Returned.Take(k).Count(outcome.Relevant.Contains) / outcome.Relevant.Count;

	/// <summary>
	/// How fast does the FIRST relevant result show up?
	/// <para>1 / its rank: rank 1 → 1.0, rank 2 → 0.5, rank 3 → 0.33, never found → 0.</para>
	/// <para>Only the first hit counts. Averaged over questions this is MRR (mean reciprocal rank).</para>
	/// </summary>
	public static double ReciprocalRank(RankedOutcome outcome) {
		for (var i = 0; i < outcome.Returned.Count; i++)
			if (outcome.Relevant.Contains(outcome.Returned[i]))
				return 1.0 / (i + 1);

		return 0.0;
	}

	/// <summary>
	/// Did the relevant memories make the top <paramref name="k"/>, and how HIGH were they ranked?
	/// <para>A relevant result at rank 1 earns full credit, further down less and less.</para>
	/// <para>1.0 = every relevant memory at the very top, 0 = none found.</para>
	/// <para>The one metric here that rewards both finding the evidence and ranking it high.
	/// (nDCG = normalized discounted cumulative gain.)</para>
	/// </summary>
	public static double NdcgAt(RankedOutcome outcome, int k) {
		var dcg = outcome.Returned
			.Take(k)
			.Select((id, i) => outcome.Relevant.Contains(id) ? Discount(i) : 0.0)
			.Sum();

		// The best possible score: every relevant memory (up to k of them) in the top positions.
		var ideal = Enumerable.Range(0, Math.Min(k, outcome.Relevant.Count)).Sum(Discount);

		return ideal == 0 ? 0.0 : dcg / ideal;
	}

	static double Discount(int index) => 1.0 / Math.Log2(index + 2);

	// Questions with no ground truth are skipped, not scored as zero: they have no defined score.
	static double Mean(IEnumerable<RankedOutcome> outcomes, Func<RankedOutcome, double> score) {
		var scored = outcomes.Where(outcome => outcome.Relevant.Count > 0).Select(score).ToList();

		return scored.Count == 0 ? 0.0 : scored.Average();
	}

	#endregion // Per-question scores
}
