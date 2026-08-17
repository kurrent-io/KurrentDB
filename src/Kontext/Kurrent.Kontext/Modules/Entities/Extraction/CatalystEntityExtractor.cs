// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Catalyst;
using Catalyst.Models;
using Mosaik.Core;
using Version = Mosaik.Core.Version;

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// Statistical NER via Catalyst's WikiNER average-perceptron model — the deterministic,
/// in-process stage that needs no network and no GPU. WikiNER emits person, organization,
/// location, and misc; misc maps to OBJECT because "some named thing" is exactly what
/// OBJECT means in POLE+O.
/// <para>Create through <see cref="CreateAsync"/> — the model loads once, the instance is
/// thread-safe for extraction, and the pipeline is shared across all calls.</para>
/// </summary>
public sealed class CatalystEntityExtractor : IEntityExtractor {
	public const string ExtractorName = "catalyst-wikiner";

	// WikiNER carries no per-entity score; the stage-level prior lives here so merge
	// strategies can rank it below pattern hits (0.95) and any future LLM stage.
	const double DefaultConfidence = 0.7;

	readonly Pipeline _pipeline;
	readonly double   _confidence;

	CatalystEntityExtractor(Pipeline pipeline, double confidence) {
		_pipeline   = pipeline;
		_confidence = confidence;
	}

	public string Name => ExtractorName;

	public static async Task<CatalystEntityExtractor> CreateAsync(double confidence = DefaultConfidence) {
		English.Register();

		var pipeline = await Pipeline.ForAsync(Language.English).ConfigureAwait(false);
		pipeline.Add(await AveragePerceptronEntityRecognizer.FromStoreAsync(Language.English, Version.Latest, "WikiNER").ConfigureAwait(false));

		return new(pipeline, confidence);
	}

	public ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default) {
		var document = new Document(text, Language.English);

		_pipeline.ProcessSingle(document);

		var entities = new List<ExtractedEntity>();

		foreach (var span in document)
			foreach (var tokens in span.GetEntities()) {
				var (type, subtype) = MapType(tokens.EntityType.Type);

				entities.Add(new() {
					Name       = tokens.Value,
					Type       = type,
					Subtype    = subtype,
					Confidence = _confidence,
					Start      = tokens.Begin,
					End        = tokens.End + 1,
					Extractor  = ExtractorName,
				});
			}

		return ValueTask.FromResult(new ExtractionResult { Entities = entities });
	}

	static (string Type, string? Subtype) MapType(string wikiNerType) => wikiNerType.ToUpperInvariant() switch {
		"PER" or "PERSON"             => (EntityTypes.Person, null),
		"ORG" or "ORGANIZATION"       => (EntityTypes.Organization, null),
		"LOC" or "LOCATION" or "GPE"  => (EntityTypes.Location, null),
		_                             => (EntityTypes.Object, "MISC"),
	};
}
