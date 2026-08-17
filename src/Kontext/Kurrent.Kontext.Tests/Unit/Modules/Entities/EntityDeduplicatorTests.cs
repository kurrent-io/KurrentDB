// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Resolution;

namespace Kurrent.Kontext.Tests.Unit.Modules.Entities;

[Category("Entities")]
public class EntityDeduplicatorTests {
	static readonly EntityRow Existing = new() {
		EntityId       = "ent-1",
		Name           = "Kurrent",
		NormalizedName = "kurrent",
		EntityType     = "ORGANIZATION",
	};

	[Test]
	public async ValueTask no_match_creates() {
		var deduplicator = new EntityDeduplicator(Fixed(EntityResolution.Unmatched));

		var decision = await deduplicator.DecideAsync(new EntityProbe("Kurrent", "ORGANIZATION"));

		await Assert.That(decision.Action).IsEqualTo(DeduplicationAction.Create);
		await Assert.That(decision.Match).IsNull();
	}

	[Test]
	public async ValueTask at_or_above_auto_merge_merges() {
		var deduplicator = new EntityDeduplicator(Fixed(new() { Match = Existing, Score = 0.95, Method = ResolutionMethod.Fuzzy }));

		var decision = await deduplicator.DecideAsync(new EntityProbe("Kurrent Inc", "ORGANIZATION"));

		await Assert.That(decision.Action).IsEqualTo(DeduplicationAction.Merge);
		await Assert.That(decision.Match!.EntityId).IsEqualTo("ent-1");
		await Assert.That(decision.Method).IsEqualTo(ResolutionMethod.Fuzzy);
	}

	[Test]
	public async ValueTask exact_matches_always_merge() {
		var deduplicator = new EntityDeduplicator(Fixed(new() { Match = Existing, Score = 1.0, Method = ResolutionMethod.Exact }));

		var decision = await deduplicator.DecideAsync(new EntityProbe("kurrent", "ORGANIZATION"));

		await Assert.That(decision.Action).IsEqualTo(DeduplicationAction.Merge);
	}

	[Test]
	public async ValueTask inside_the_flag_band_flags() {
		var deduplicator = new EntityDeduplicator(Fixed(new() { Match = Existing, Score = 0.88, Method = ResolutionMethod.Semantic }));

		var decision = await deduplicator.DecideAsync(new EntityProbe("Kurrent.io", "ORGANIZATION"));

		await Assert.That(decision.Action).IsEqualTo(DeduplicationAction.Flag);
		await Assert.That(decision.Match!.EntityId).IsEqualTo("ent-1");
		await Assert.That(decision.Score).IsEqualTo(0.88);
	}

	[Test]
	public async ValueTask below_the_flag_band_creates() {
		var deduplicator = new EntityDeduplicator(Fixed(new() { Match = Existing, Score = 0.80, Method = ResolutionMethod.Semantic }));

		var decision = await deduplicator.DecideAsync(new EntityProbe("Kurrent Cloud", "ORGANIZATION"));

		await Assert.That(decision.Action).IsEqualTo(DeduplicationAction.Create);
	}

	[Test]
	public async ValueTask custom_thresholds_move_the_lines() {
		var options = new EntityDeduplicationOptions { AutoMergeThreshold = 0.9, FlagThreshold = 0.7 };

		var deduplicator = new EntityDeduplicator(
			Fixed(new() { Match = Existing, Score = 0.91, Method = ResolutionMethod.Fuzzy }), options);

		var decision = await deduplicator.DecideAsync(new EntityProbe("Kurrent Inc", "ORGANIZATION"));

		await Assert.That(decision.Action).IsEqualTo(DeduplicationAction.Merge);
	}

	[Test]
	public async ValueTask inverted_or_out_of_range_thresholds_are_rejected() {
		await Assert.That(() => new EntityDeduplicator(
				Fixed(EntityResolution.Unmatched),
				new() { AutoMergeThreshold = 0.8, FlagThreshold = 0.9 }))
			.Throws<ArgumentException>();

		await Assert.That(() => new EntityDeduplicator(
				Fixed(EntityResolution.Unmatched),
				new() { AutoMergeThreshold = 1.2, FlagThreshold = 0.9 }))
			.Throws<ArgumentException>();
	}

	static IEntityResolver Fixed(EntityResolution resolution) => new FixedResolver(resolution);

	sealed class FixedResolver(EntityResolution resolution) : IEntityResolver {
		public ValueTask<EntityResolution> ResolveAsync(ResolutionProbe probe, CancellationToken ct = default) =>
			ValueTask.FromResult(resolution);
	}
}
