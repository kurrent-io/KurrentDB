// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Entities.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace Kurrent.Kontext.Tests.Modules.Entities;

/// <summary>
/// The extraction merge as a whole: a stub extractor stands in for the NER model so the pipeline's
/// own work — splitting coordinations, filtering, ordering by first appearance — is what is under
/// test, not the model's opinions.
/// </summary>
public class EntityExtractorPipelineTests {
	const string Content = "counseling and support groups with Luna & Oliver, sunflowers, roses and mental health, rock and 5";

	static readonly ExtractedEntity[] Spans = [
		new("counseling and support groups", "organization", 0.9),
		new("Luna & Oliver", "animal", 0.8),
		new("sunflowers, roses", "object", 0.7),
		new("mental health", "health condition", 0.6),
		new("rock and 5", "object", 0.5),
	];

	[Test]
	public async ValueTask a_coordinated_span_becomes_the_entities_it_names() {
		// Act — flat NER scores one span per range and drops the losers, so the pipeline is the
		// only place the entities inside a coordination can still be recovered.
		var entities = await Pipeline(split: true).ExtractAsync(Content);

		// Assert — the parts replace the whole, in the order the content mentions them. An
		// uncoordinated span is untouched, and "rock and 5" survives whole because "5" cannot name
		// anything: one unusable part means the span was never a clean coordination, and splitting
		// it anyway would invent entities.
		await Assert.That(entities.Select(entity => entity.Text).ToList()).IsEquivalentTo([
			"counseling", "support groups", "Luna", "Oliver", "sunflowers", "roses", "mental health", "rock and 5",
		], CollectionOrdering.Matching);

		// Assert — a part inherits its parent's type and confidence; nothing is re-scored.
		await Assert.That(entities[2] with { }).IsEqualTo(new ExtractedEntity("Luna", "animal", 0.8));
		await Assert.That(entities[3] with { }).IsEqualTo(new ExtractedEntity("Oliver", "animal", 0.8));
	}

	[Test]
	public async ValueTask splitting_off_leaves_every_span_as_the_extractor_scored_it() {
		// Act — the composition the benchmark prices splitting against.
		var entities = await Pipeline(split: false).ExtractAsync(Content);

		// Assert
		await Assert.That(entities.Select(entity => entity.Text).ToList())
			.IsEquivalentTo(Spans.Select(span => span.Text).ToList(), CollectionOrdering.Matching);
	}

	static EntityExtractor.Pipeline Pipeline(bool split) =>
		new([new StubExtractor()],
			NullLogger<EntityExtractor.Pipeline>.Instance,
			new EntityExtractor.PipelineOptions { SplitCoordinatedSpans = split });

	sealed class StubExtractor : IEntityExtractor {
		public ValueTask<IReadOnlyList<ExtractedEntity>> ExtractAsync(string content, CancellationToken ct = default) =>
			ValueTask.FromResult<IReadOnlyList<ExtractedEntity>>(Spans);
	}
}
