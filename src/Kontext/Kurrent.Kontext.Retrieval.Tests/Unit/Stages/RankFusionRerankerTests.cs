// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class RankFusionRerankerTests {
	[Test]
	public async ValueTask fuses_incoming_and_model_ranks() {
		var pool  = Pool(3);
		var model = new FakeRelevanceModel(0.1, 0.9, 0.5);

		var refined = await RankFusionReranker<NativeScale>.Create(model).Run(pool);

		// incoming ranks m01 m02 m03, model ranks m02 m03 m01:
		// m02 = 1/62 + 1/61, m01 = 1/61 + 1/63, m03 = 1/63 + 1/62
		await Assert.That(Fixtures.Ids(refined)).IsEquivalentTo(["m02", "m01", "m03"], CollectionOrdering.Matching);
		await Assert.That(refined[0].Score).IsEqualTo((1.0 / 62 + 1.0 / 61) * 61 / 2).Within(1e-12);
		await Assert.That(refined[0].Breakdown.Reranked).IsEqualTo(0.9);
	}

	[Test]
	public async ValueTask drops_candidates_outside_both_top_pools() {
		var pool = Pool(25);

		// The model agrees with the incoming order, so both top-20s are the first 20 members.
		var model = new FakeRelevanceModel(Enumerable.Range(1, 25).Select(i => 1.0 - i * 0.01).ToArray());

		var refined = await RankFusionReranker<NativeScale>.Create(model).Run(pool);

		await Assert.That(Fixtures.Ids(refined)).IsEquivalentTo(pool.Take(20).Select(scored => scored.Memory.MemoryId).ToList(), CollectionOrdering.Matching);
	}

	static IReadOnlyList<ScoredMemory> Pool(int count) =>
		Enumerable.Range(1, count).Select(i => Fixtures.Scored($"m{i:d2}", 1.0 - i * 0.01, $"passage {i}")).ToList();
}
