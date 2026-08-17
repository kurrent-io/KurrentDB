// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Extraction;

namespace Kurrent.Kontext.Tests.Unit.Modules.Entities;

[Category("Entities")]
public class EntityExtractionPipelineTests {
	[Test]
	public async ValueTask union_keeps_all_uniques_and_highest_confidence_on_collision() {
		var pipeline = EntityExtractionPipeline.From([
			Fixed("a", Person("Ada Lovelace", 0.6), Org("Kurrent", 0.9)),
			Fixed("b", Person("Ada Lovelace", 0.8), Location("London", 0.7)),
		]);

		var result = await pipeline.ExtractAsync("text");

		await Assert.That(result.Entities).Count().IsEqualTo(3);

		var ada = result.Entities.Single(entity => entity.Type == EntityTypes.Person);
		await Assert.That(ada.Confidence).IsEqualTo(0.8);
		await Assert.That(ada.Extractor).IsEqualTo("b");
	}

	[Test]
	public async ValueTask intersection_keeps_consensus_only_and_boosts_confidence() {
		var pipeline = EntityExtractionPipeline.From([
			Fixed("a", Person("Ada Lovelace", 0.6), Org("Kurrent", 0.9)),
			Fixed("b", Person("Ada Lovelace", 0.8)),
		], static options => options.Merger = new IntersectionMerger());

		var result = await pipeline.ExtractAsync("text");

		var ada = await Assert.That(result.Entities).HasSingleItem();
		await Assert.That(ada!.NormalizedName).IsEqualTo("ada lovelace");
		await Assert.That(ada.Confidence).IsEqualTo(Math.Min(1.0, 0.8 * 1.1));
	}

	[Test]
	public async ValueTask intersection_ignores_repeats_within_one_stage() {
		var pipeline = EntityExtractionPipeline.From([
			Fixed("a", Person("Ada Lovelace", 0.6), Person("Ada Lovelace", 0.7)),
			Fixed("b", Org("Kurrent", 0.9)),
		], static options => options.Merger = new IntersectionMerger());

		var result = await pipeline.ExtractAsync("text");

		await Assert.That(result.Entities).IsEmpty();
	}

	[Test]
	public async ValueTask cascade_lets_the_first_stage_win_and_later_stages_fill_gaps() {
		var pipeline = EntityExtractionPipeline.From([
			Fixed("a", Person("Ada Lovelace", 0.6)),
			Fixed("b", Person("Ada Lovelace", 0.99), Org("Kurrent", 0.9)),
		], static options => options.Merger = new CascadeMerger());

		var result = await pipeline.ExtractAsync("text");

		await Assert.That(result.Entities).Count().IsEqualTo(2);

		var ada = result.Entities.Single(entity => entity.Type == EntityTypes.Person);
		await Assert.That(ada.Extractor).IsEqualTo("a");
		await Assert.That(ada.Confidence).IsEqualTo(0.6);
	}

	[Test]
	public async ValueTask first_success_skips_the_remaining_stages() {
		var second = new CountingExtractor(Fixed("b", Org("Kurrent", 0.9)));

		var pipeline = EntityExtractionPipeline.From(
			[Fixed("a", Person("Ada Lovelace", 0.6)), second],
			static options => options.Merger = new FirstSuccessMerger());

		var result = await pipeline.ExtractAsync("text");

		await Assert.That(result.Entities).HasSingleItem();
		await Assert.That(second.Calls).IsEqualTo(0);
	}

	[Test]
	public async ValueTask stop_on_success_honors_the_minimum_entity_count() {
		var second = new CountingExtractor(Fixed("b", Org("Kurrent", 0.9), Location("London", 0.7)));

		var pipeline = EntityExtractionPipeline.From(
			[Fixed("a", Person("Ada Lovelace", 0.6)), second],
			static options => {
				options.StopOnSuccess         = true;
				options.MinEntitiesForSuccess = 2;
			});

		var result = await pipeline.ExtractAsync("text");

		// The first stage's single entity was not enough, so the second stage ran and stopped the walk.
		await Assert.That(second.Calls).IsEqualTo(1);
		await Assert.That(result.Entities).Count().IsEqualTo(3);
	}

	[Test]
	public async ValueTask failing_stage_is_skipped_by_default() {
		var pipeline = EntityExtractionPipeline.From([new ThrowingExtractor(), Fixed("b", Org("Kurrent", 0.9))]);

		var result = await pipeline.ExtractAsync("text");

		await Assert.That(result.Entities).HasSingleItem();
	}

	[Test]
	public async ValueTask fail_on_error_rethrows_the_stage_failure() {
		var pipeline = EntityExtractionPipeline.From(
			[new ThrowingExtractor()],
			static options => options.FallbackOnError = false);

		await Assert.That(async () => await pipeline.ExtractAsync("text")).Throws<InvalidOperationException>();
	}

	[Test]
	public async ValueTask invalid_surface_forms_are_filtered_after_the_merge() {
		var pipeline = EntityExtractionPipeline.From([Fixed("a", Person("they", 0.9), Person("Ada Lovelace", 0.9), Org("42", 0.9))]);

		var result = await pipeline.ExtractAsync("text");

		var ada = await Assert.That(result.Entities).HasSingleItem();
		await Assert.That(ada!.Name).IsEqualTo("Ada Lovelace");
	}

	[Test]
	public async ValueTask blank_text_short_circuits_to_empty() {
		var stage = new CountingExtractor(Fixed("a", Person("Ada Lovelace", 0.9)));

		var pipeline = EntityExtractionPipeline.From([stage]);

		var result = await pipeline.ExtractAsync("   ");

		await Assert.That(result.Entities).IsEmpty();
		await Assert.That(stage.Calls).IsEqualTo(0);
	}

	static ExtractedEntity Person(string name, double confidence) =>
		new() { Name = name, Type = EntityTypes.Person, Confidence = confidence };

	static ExtractedEntity Org(string name, double confidence) =>
		new() { Name = name, Type = EntityTypes.Organization, Confidence = confidence };

	static ExtractedEntity Location(string name, double confidence) =>
		new() { Name = name, Type = EntityTypes.Location, Confidence = confidence };

	static FixedExtractor Fixed(string name, params ExtractedEntity[] entities) =>
		new(name, [.. entities.Select(entity => entity with { Extractor = name })]);

	sealed class FixedExtractor(string name, ExtractedEntity[] entities) : IEntityExtractor {
		public string Name => name;

		public ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default) =>
			ValueTask.FromResult(new ExtractionResult { Entities = entities });
	}

	sealed class CountingExtractor(IEntityExtractor inner) : IEntityExtractor {
		public int Calls { get; private set; }

		public string Name => inner.Name;

		public ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default) {
			Calls++;
			return inner.ExtractAsync(text, ct);
		}
	}

	sealed class ThrowingExtractor : IEntityExtractor {
		public string Name => "throwing";

		public ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default) =>
			throw new InvalidOperationException("boom");
	}
}
