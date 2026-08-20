// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class BoostedModulatorTests {
	[Test]
	public async ValueTask boosts_never_overturn_relevance() {
		// leader takes the worst-case penalties (stale ×0.9, low importance ×0.95 → 0.855) while
		// chaser takes the maximum boosts (fresh ×1.1, critical ×1.1 → 0.6 × 1.21 = 0.726)
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("leader", 1.0, importance: MemoryContracts.MemoryImportance.Low, age: TimeSpan.FromDays(3650)),
			Fixtures.Scored("chaser", 0.6, importance: MemoryContracts.MemoryImportance.Critical, age: TimeSpan.Zero),
			Fixtures.Scored("floor", 0.0),
		];

		var result = await BoostedModulator.Create().ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(Fixtures.Ids(result)[0]).IsEqualTo("leader");
	}

	[Test]
	public async ValueTask degenerate_pool_seeds_from_rank() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("stale", 0.7, age: TimeSpan.FromDays(3650)),
			Fixtures.Scored("fresh", 0.7, age: TimeSpan.Zero),
			Fixtures.Scored("mid", 0.7, age: TimeSpan.FromDays(90)),
		];

		var result = await BoostedModulator.Create().ProcessAsync(Fixtures.Query(), pool);

		// rank seeds 1.0 / 0.55 / 0.1 keep the incoming order despite fresh being maximally fresh
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["stale", "fresh", "mid"], CollectionOrdering.Matching);
		await Assert.That(result[0].Breakdown.BaseScore!.Value).IsEqualTo(1.0);
		await Assert.That(result[1].Breakdown.BaseScore!.Value).IsEqualTo(0.55).Within(1e-12);
		await Assert.That(result[2].Breakdown.BaseScore!.Value).IsEqualTo(0.1).Within(1e-12);
	}
}
