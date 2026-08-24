// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Benchmarks.Entities;

/// <summary>The result tables bypass the logger on purpose — they are the deliverable, not log events.</summary>
static class EntityResolutionReport {
	public static void PrintMetrics(IReadOnlyList<ResolutionRun> runs, int labelledClusters) {
		Console.WriteLine();
		Console.WriteLine($@"{"run",-24} {"precision",10} {"recall",8} {"f1",8} {"wrong",7} {"missed",7} {"entities",9} {"bloat",7}");

		foreach (var run in runs)
			Console.WriteLine(
				$@"{run.Name,-24} {run.Precision,10:P1} {run.Recall,8:P1} {run.F1,8:P1} " +
				$@"{run.FalsePositives,7} {run.FalseNegatives,7} {run.DistinctEntities,9} " +
				$@"{(double)run.DistinctEntities / labelledClusters,7:P0}");

		Console.WriteLine();
		Console.WriteLine($@"labelled: {labelledClusters} clusters, {runs[0].Verdicts.Count} same-type pairs ({runs[0].SameEntityPairs} of them the same thing)");
		Console.WriteLine($@"bloat: entities the catalog holds for {labelledClusters} real things — 100% is one entity each, higher is fragmentation");
	}

	/// <summary>The actionable half: every wrong and missed merge, named, with the tier that decided it.</summary>
	public static void PrintErrors(ResolutionRun run) {
		var wrong = run.Verdicts.Where(verdict => verdict.FalsePositive).ToList();

		Console.WriteLine();
		Console.WriteLine($@"=== {run.Name}");
		Console.WriteLine();
		Console.WriteLine($@"wrong merges ({wrong.Count}) — different things the resolver put together:");

		foreach (var verdict in wrong.OrderBy(verdict => verdict.Pair.Type).ThenBy(verdict => verdict.Pair.Left, StringComparer.Ordinal))
			Console.WriteLine($@"  {verdict.Pair.Type,-16} {verdict.Pair.Left,-24} = {verdict.Pair.Right,-24} via {Tier(verdict)}");

		var missed = run.Verdicts.Where(verdict => verdict.FalseNegative).ToList();

		Console.WriteLine();
		Console.WriteLine($@"missed merges ({missed.Count}) — the same thing the resolver left apart:");

		foreach (var verdict in missed.OrderBy(verdict => verdict.Pair.Type).ThenBy(verdict => verdict.Pair.Left, StringComparer.Ordinal))
			Console.WriteLine($@"  {verdict.Pair.Type,-16} {verdict.Pair.Left,-24} | {verdict.Pair.Right,-24} via {Tier(verdict)}");

		if (run.Fragmented.Count > 0) {
			Console.WriteLine();
			Console.WriteLine($@"fragmented clusters ({run.Fragmented.Count}) — one thing spread over several entities:");

			foreach (var (cluster, type, pieces) in run.Fragmented.OrderByDescending(entry => entry.Pieces))
				Console.WriteLine($@"  {type,-16} {cluster,-24} {pieces} pieces");
		}

		Console.WriteLine();
		Console.WriteLine("resolutions by tier:");

		foreach (var (method, count) in run.ByMethod.OrderByDescending(entry => entry.Value))
			Console.WriteLine($@"  {method,-32} {count,4}");
	}

	// Which tier decided the pair: the later of the two forms carries the decision, since the
	// earlier one was resolved against a catalog that did not yet hold it.
	static string Tier(PairVerdict verdict) =>
		$"{verdict.Right.Method} {verdict.Right.Confidence:F2}";
}
