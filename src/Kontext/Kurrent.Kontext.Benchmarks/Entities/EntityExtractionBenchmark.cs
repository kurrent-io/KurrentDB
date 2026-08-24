// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Testing;

namespace Benchmarks.Entities;

/// <summary>
/// Scores extraction against labelled memories: precision, recall and F1 per entity type, micro-
/// and macro-averaged, beside latency and throughput. Modelled on the neo4j agent-memory
/// extraction benchmark (<c>benchmarks/metrics.py</c>) — same per-type/micro/macro shape and the
/// same greedy one-to-one matching, so a duplicate span cannot count as two hits.
/// <para>Two deviations, both deliberate. Their matcher demands an exact type match; a zero-shot
/// label set makes that a coin toss ("pottery" is defensibly a creative work, an activity or an
/// object), so a label here carries every defensible type and the run also reports an UNTYPED
/// score that ignores type. The gap between the two is the type-confusion rate, which their
/// single number hides.</para>
/// </summary>
sealed class EntityExtractionBenchmark(EntityExtractionLabels labels) {
	public async ValueTask<ExtractionRun> Run(string name, IEntityExtractor extractor) {
		// Warmup, untimed: the first call pays model load and JIT.
		await extractor.ExtractAsync(labels.Documents[0].Text);

		var outcomes = new List<DocumentOutcome>(labels.Documents.Count);
		var elapsed  = TimeSpan.Zero;

		using var folder = new EntityFolder();

		foreach (var document in labels.Documents) {
			var stopwatch = Stopwatch.StartNew();
			var extracted = await extractor.ExtractAsync(document.Text);
			stopwatch.Stop();

			elapsed += stopwatch.Elapsed;
			outcomes.Add(Match(document, extracted, folder));
		}

		return new(name, outcomes, elapsed);
	}

	/// <summary>
	/// Greedy one-to-one: each extracted span claims the first unclaimed label it matches, so two
	/// spans of the same name cannot both score. Unclaimed spans are false positives, unclaimed
	/// labels false negatives.
	/// </summary>
	static DocumentOutcome Match(LabelledDocument document, IReadOnlyList<ExtractedEntity> extracted, EntityFolder folder) {
		var typedHits   = new List<(ExtractedEntity Span, ExpectedEntity Label)>();
		var untypedHits = new List<(ExtractedEntity Span, ExpectedEntity Label)>();
		var spurious    = new List<ExtractedEntity>();

		var claimedTyped   = new HashSet<int>();
		var claimedUntyped = new HashSet<int>();

		foreach (var span in extracted) {
			var typed   = Claim(document.Expected, claimedTyped, span, folder, requireType: true);
			var untyped = Claim(document.Expected, claimedUntyped, span, folder, requireType: false);

			if (typed is { } typedIndex)
				typedHits.Add((span, document.Expected[typedIndex]));

			if (untyped is { } untypedIndex)
				untypedHits.Add((span, document.Expected[untypedIndex]));
			else
				spurious.Add(span);
		}

		var missed = document.Expected
			.Where((_, index) => !claimedUntyped.Contains(index))
			.ToList();

		return new(document, extracted, typedHits, untypedHits, spurious, missed);
	}

	static int? Claim(
		IReadOnlyList<ExpectedEntity> expected, HashSet<int> claimed, ExtractedEntity span, EntityFolder folder, bool requireType
	) {
		for (var index = 0; index < expected.Count; index++) {
			if (claimed.Contains(index))
				continue;

			var label = expected[index];

			// Compare on the fold, not the raw string: that is the key the catalog and the entity
			// leg actually match on, so "The sign" counts as finding "sign". Matching raw strings
			// would report a miss the pipeline does not have.
			if (folder.Fold(label.Name) != folder.Fold(span.Text))
				continue;

			if (requireType && !label.AcceptsType(span.EntityType))
				continue;

			claimed.Add(index);
			return index;
		}

		return null;
	}
}

sealed record DocumentOutcome(
	LabelledDocument Document,
	IReadOnlyList<ExtractedEntity> Extracted,
	IReadOnlyList<(ExtractedEntity Span, ExpectedEntity Label)> TypedHits,
	IReadOnlyList<(ExtractedEntity Span, ExpectedEntity Label)> UntypedHits,
	IReadOnlyList<ExtractedEntity> Spurious,
	IReadOnlyList<ExpectedEntity> Missed
);

/// <summary>Precision, recall and F1 for one slice — a type, or the whole run.</summary>
readonly record struct Score(int TruePositives, int FalsePositives, int FalseNegatives) {
	public double Precision => TruePositives + FalsePositives is var found and > 0 ? (double)TruePositives / found : 0;
	public double Recall    => TruePositives + FalseNegatives is var wanted and > 0 ? (double)TruePositives / wanted : 0;
	public double F1        => Precision + Recall is var sum and > 0 ? 2 * Precision * Recall / sum : 0;
}

sealed record ExtractionRun(string Name, IReadOnlyList<DocumentOutcome> Outcomes, TimeSpan Elapsed) {
	public int Extracted => Outcomes.Sum(outcome => outcome.Extracted.Count);
	public int Expected  => Outcomes.Sum(outcome => outcome.Document.Expected.Count);

	/// <summary>Right span AND a defensible type.</summary>
	public Score Typed => new(
		Outcomes.Sum(outcome => outcome.TypedHits.Count),
		Extracted - Outcomes.Sum(outcome => outcome.TypedHits.Count),
		Expected - Outcomes.Sum(outcome => outcome.TypedHits.Count));

	/// <summary>Right span, whatever the type — what the entity leg actually matches on.</summary>
	public Score Untyped => new(
		Outcomes.Sum(outcome => outcome.UntypedHits.Count),
		Outcomes.Sum(outcome => outcome.Spurious.Count),
		Outcomes.Sum(outcome => outcome.Missed.Count));

	/// <summary>Spans found under a type no label accepts — the cost of an open zero-shot vocabulary.</summary>
	public int TypeConfusions => Untyped.TruePositives - Typed.TruePositives;

	public double MeanMs => Elapsed.TotalMilliseconds / Outcomes.Count;

	public double DocumentsPerSecond => Outcomes.Count / Elapsed.TotalSeconds;

	/// <summary>Per-label-type scores, keyed by the label's canonical type — the macro average's input.</summary>
	public IReadOnlyDictionary<string, Score> ByType {
		get {
			var counts = new Dictionary<string, (int Tp, int Fn)>(StringComparer.OrdinalIgnoreCase);

			foreach (var outcome in Outcomes) {
				foreach (var (_, label) in outcome.UntypedHits)
					Add(label.Types[0], hit: true);

				foreach (var label in outcome.Missed)
					Add(label.Types[0], hit: false);
			}

			// False positives have no label to take a canonical type from, so they are charged to
			// the type the extractor itself claimed.
			var spurious = Outcomes
				.SelectMany(outcome => outcome.Spurious)
				.GroupBy(span => span.EntityType, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

			return counts.Keys
				.Union(spurious.Keys, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					type => type,
					type => new Score(
						counts.GetValueOrDefault(type).Tp,
						spurious.GetValueOrDefault(type),
						counts.GetValueOrDefault(type).Fn),
					StringComparer.OrdinalIgnoreCase);

			void Add(string type, bool hit) {
				var (tp, fn) = counts.GetValueOrDefault(type);
				counts[type] = hit ? (tp + 1, fn) : (tp, fn + 1);
			}
		}
	}

	/// <summary>Every type weighted equally, so a rare type failing is not hidden by a common one succeeding.</summary>
	public double MacroPrecision => ByType.Values.Average(score => score.Precision);
	public double MacroRecall    => ByType.Values.Average(score => score.Recall);

	public double MacroF1 =>
		MacroPrecision + MacroRecall is var sum and > 0
			? 2 * MacroPrecision * MacroRecall / sum
			: 0;
}
