// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Testing;

namespace Benchmarks.Retrieval;

internal sealed record QuestionOutcome(CorpusQuestion Question, RankedOutcome Outcome, TimeSpan Elapsed);

internal sealed record QualityRun(string Name, IReadOnlyList<QuestionOutcome> Outcomes) {
	IEnumerable<RankedOutcome> Ranked => Outcomes.Select(outcome => outcome.Outcome);

	public double RecallAt(int k) => RankingMetrics.RecallAt(Ranked, k);

	public double Mrr => RankingMetrics.Mrr(Ranked);

	public double NdcgAt(int k) => RankingMetrics.NdcgAt(Ranked, k);

	public double MeanMs => Outcomes.Average(outcome => outcome.Elapsed.TotalMilliseconds);
}
