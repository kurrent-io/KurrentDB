// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// Deterministic regex extraction for the machine-shaped identifiers agent memories are full of —
/// URLs and emails — which statistical NER models reliably mangle. High confidence by
/// construction: a regex hit is not a guess.
/// </summary>
public sealed partial class PatternEntityExtractor : IEntityExtractor {
	public const string ExtractorName = "pattern";

	const double PatternConfidence = 0.95;

	public string Name => ExtractorName;

	public ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default) {
		var entities = new List<ExtractedEntity>();

		foreach (Match match in UrlPattern().Matches(text))
			entities.Add(Entity(match, "URL"));

		foreach (Match match in EmailPattern().Matches(text))
			entities.Add(Entity(match, "EMAIL"));

		return ValueTask.FromResult(new ExtractionResult { Entities = entities });
	}

	static ExtractedEntity Entity(Match match, string subtype) => new() {
		Name       = match.Value,
		Type       = EntityTypes.Object,
		Subtype    = subtype,
		Confidence = PatternConfidence,
		Start      = match.Index,
		End        = match.Index + match.Length,
		Extractor  = ExtractorName,
	};

	// Trailing sentence punctuation is excluded so "see https://kurrent.io." captures the URL
	// without the period.
	[GeneratedRegex(@"\bhttps?://[^\s<>""')\]]+[^\s<>""')\].,;:!?]")]
	private static partial Regex UrlPattern();

	[GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
	private static partial Regex EmailPattern();
}
