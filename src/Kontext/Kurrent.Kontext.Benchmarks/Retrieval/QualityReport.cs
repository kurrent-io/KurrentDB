// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Kurrent.Kontext.Testing;

namespace Benchmarks.Retrieval;

/// <summary>The result tables bypass the logger on purpose — they are the deliverable, not log events.</summary>
internal static class QualityReport {
	public static void Dump(QualityRun run, string path) {
		var outcomes = run.Outcomes.Select(outcome => new {
			question = outcome.Question.Question,
			category = outcome.Question.Category,
			relevant = outcome.Question.Relevant,
			returned = outcome.Outcome.Returned,
		});

		File.WriteAllText(path, JsonSerializer.Serialize(new { name = run.Name, outcomes }));
	}

	public static void PrintMetrics(IReadOnlyList<QualityRun> runs, QualityRun baseline) {
		Console.WriteLine();
		Console.WriteLine($@"{"composition",-28} {"recall@1",9} {"recall@5",9} {"recall@10",10} {"mrr",8} {"ndcg@10",9} {"vs base",10} {"mean ms",9}");

		foreach (var run in runs.OrderByDescending(run => run.NdcgAt(10)))
			Console.WriteLine(
				$@"{run.Name,-28} {run.RecallAt(1),9:F4} {run.RecallAt(5),9:F4} {run.RecallAt(10),10:F4} " +
				$@"{run.Mrr,8:F4} {run.NdcgAt(10),9:F4} {run.NdcgAt(10) - baseline.NdcgAt(10),10:+0.0000;-0.0000} {run.MeanMs,9:F1}");
	}

	public static void PrintHeadToHead(QualityRun baseline, QualityRun candidate) {
		var pairs = baseline.Outcomes
			.Zip(candidate.Outcomes, (a, b) => (a.Question, Baseline: a, Candidate: b, Delta: RankingMetrics.NdcgAt(b.Outcome, 10) - RankingMetrics.NdcgAt(a.Outcome, 10)))
			.ToList();

		var wins    = pairs.Count(pair => pair.Delta > 0);
		var losses  = pairs.Count(pair => pair.Delta < 0);
		var ties    = pairs.Count(pair => pair.Delta == 0);
		var overlap = pairs.Average(pair => Jaccard(pair.Baseline.Outcome.Returned, pair.Candidate.Outcome.Returned));

		Console.WriteLine();
		Console.WriteLine($@"head-to-head on per-question ndcg@10: {candidate.Name} wins {wins}, {baseline.Name} wins {losses}, ties {ties}");
		Console.WriteLine($@"mean top-{RetrievalQualityBenchmark.Limit} jaccard overlap between the two rankings: {overlap:F3}");

		PrintDivergences($"largest wins for {candidate.Name}", pairs.Where(pair => pair.Delta > 0).OrderByDescending(pair => pair.Delta));
		PrintDivergences($"largest wins for {baseline.Name}", pairs.Where(pair => pair.Delta < 0).OrderBy(pair => pair.Delta));
	}

	static void PrintDivergences(string title, IEnumerable<(CorpusQuestion Question, QuestionOutcome Baseline, QuestionOutcome Candidate, double Delta)> pairs) {
		Console.WriteLine();
		Console.WriteLine($@"{title}:");

		foreach (var pair in pairs.Take(5))
			Console.WriteLine($@"  {pair.Delta,8:+0.0000;-0.0000}  first hit {FirstHit(pair.Baseline)} -> {FirstHit(pair.Candidate)}  {Truncate(pair.Question.Question, 80)}");
	}

	static string FirstHit(QuestionOutcome outcome) {
		var rank = RankingMetrics.ReciprocalRank(outcome.Outcome);
		return rank == 0 ? "miss" : $"#{(int)Math.Round(1 / rank)}";
	}

	static string Truncate(string text, int length) =>
		text.Length <= length ? text : text[..(length - 1)] + "…";

	static double Jaccard(IReadOnlyList<string> a, IReadOnlyList<string> b) {
		var setA = a.ToHashSet();
		var setB = b.ToHashSet();

		var union = setA.Union(setB).Count();

		return union == 0 ? 1.0 : (double)setA.Intersect(setB).Count() / union;
	}
}
