// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Benchmarks.Entities;

/// <summary>The result tables bypass the logger on purpose — they are the deliverable, not log events.</summary>
static class EntityExtractionReport {
	public static void PrintMetrics(IReadOnlyList<ExtractionRun> runs) {
		Console.WriteLine();
		Console.WriteLine($@"{"extractor",-26} {"precision",10} {"recall",8} {"f1",8} {"macro f1",9} {"typed f1",9} {"ms/doc",8} {"docs/s",8}");

		foreach (var run in runs)
			Console.WriteLine(
				$@"{run.Name,-26} {run.Untyped.Precision,10:P1} {run.Untyped.Recall,8:P1} {run.Untyped.F1,8:P1} " +
				$@"{run.MacroF1,9:P1} {run.Typed.F1,9:P1} {run.MeanMs,8:F1} {run.DocumentsPerSecond,8:F1}");

		Console.WriteLine();
		Console.WriteLine("precision/recall/f1 are UNTYPED (right span, any type) — what the entity leg matches on.");
		Console.WriteLine("macro f1 weights every type equally; typed f1 additionally demands a defensible type.");
	}

	public static void PrintDetail(ExtractionRun run) {
		Console.WriteLine();
		Console.WriteLine($@"=== {run.Name}");
		Console.WriteLine();
		Console.WriteLine($@"{run.Extracted} spans extracted for {run.Expected} labelled, {run.TypeConfusions} found under an unlabelled type");

		Console.WriteLine();
		Console.WriteLine($@"{"type",-18} {"precision",10} {"recall",8} {"f1",8} {"found",7} {"missed",7}");

		foreach (var (type, score) in run.ByType.OrderByDescending(entry => entry.Value.TruePositives + entry.Value.FalseNegatives))
			Console.WriteLine(
				$@"{type,-18} {score.Precision,10:P1} {score.Recall,8:P1} {score.F1,8:P1} " +
				$@"{score.TruePositives,7} {score.FalseNegatives,7}");

		var missed = run.Outcomes
			.SelectMany(outcome => outcome.Missed.Select(label => (outcome.Document.MemoryId, label.Name, label.Types[0])))
			.ToList();

		Console.WriteLine();
		Console.WriteLine($@"missed ({missed.Count}) — labelled entities extraction did not find:");

		foreach (var (memoryId, name, type) in missed.Take(25))
			Console.WriteLine($@"  {memoryId,-10} {type,-16} {name}");

		if (missed.Count > 25)
			Console.WriteLine($@"  ... and {missed.Count - 25} more");

		var spurious = run.Outcomes
			.SelectMany(outcome => outcome.Spurious.Select(span => (outcome.Document.MemoryId, span.Text, span.EntityType)))
			.ToList();

		Console.WriteLine();
		Console.WriteLine($@"spurious ({spurious.Count}) — spans extraction found that no label wanted:");

		foreach (var (memoryId, text, type) in spurious.Take(25))
			Console.WriteLine($@"  {memoryId,-10} {type,-16} {text}");

		if (spurious.Count > 25)
			Console.WriteLine($@"  ... and {spurious.Count - 25} more");
	}
}
