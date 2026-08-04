// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Planning;

[Category("Planning")]
public class OverfetchOptionsTests {
	[Test]
	public async ValueTask floor_wins_below_the_crossover() {
		var overfetch = new OverfetchOptions();

		// limit 5 * Factor 3 = 15, below Floor 30 -> the floor wins.
		await Assert.That(overfetch.PoolSizeFor(5)).IsEqualTo(30);
	}

	[Test]
	public async ValueTask factor_wins_above_the_crossover() {
		var overfetch = new OverfetchOptions();

		// limit 100 * Factor 3 = 300, above Floor 30 -> the factor wins.
		await Assert.That(overfetch.PoolSizeFor(100)).IsEqualTo(100 * 3);
	}

	[Test]
	public async ValueTask factor_and_floor_agree_exactly_at_the_crossover() {
		var overfetch = new OverfetchOptions();

		// limit 10 * Factor 3 = 30 = Floor 30 -> both paths land on the same value.
		await Assert.That(overfetch.PoolSizeFor(10)).IsEqualTo(30);
	}

	[Test]
	public async ValueTask zero_factor_collapses_to_the_floor_as_wired_by_the_legacy_pipeline() {
		// AddLegacyPipeline pins exactly this: Factor = 0, Floor = 30.
		var overfetch = new OverfetchOptions { Factor = 0, Floor = 30 };

		await Assert.That(overfetch.PoolSizeFor(10)).IsEqualTo(30);
		await Assert.That(overfetch.PoolSizeFor(1_000)).IsEqualTo(30);
	}

	[Test]
	public async ValueTask non_positive_limit_still_returns_at_least_the_floor() {
		var overfetch = new OverfetchOptions();

		await Assert.That(overfetch.PoolSizeFor(0)).IsEqualTo(30);
		await Assert.That(overfetch.PoolSizeFor(-5)).IsEqualTo(30);
	}
}
