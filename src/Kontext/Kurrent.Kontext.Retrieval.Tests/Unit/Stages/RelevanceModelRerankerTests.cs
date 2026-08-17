// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class RelevanceModelRerankerTests {
	[Test]
	public async ValueTask reranks_head_keeps_tail_order() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.9),
			Fixtures.Scored("b", 0.8),
			Fixtures.Scored("c", 0.7),
			Fixtures.Scored("d", 0.6),
		];

		var reranker = RelevanceModelReranker<NativeScale>.Create(new FakeRelevanceModel(0.1, 0.9), options => options.CandidateCap = 2);
		var result   = await reranker.Run(pool);

		// the model demotes a below b inside the head; the tail keeps its order below both
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["b", "a", "c", "d"], CollectionOrdering.Matching);
		await Assert.That(result[0].Breakdown.Reranked).IsEqualTo(0.9);
		await Assert.That(result[1].Breakdown.Reranked).IsEqualTo(0.1);
		await Assert.That(result[2].Breakdown.Reranked).IsNull();
	}

	[Test]
	public async ValueTask throws_on_score_count_mismatch() {
		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("a", 0.9), Fixtures.Scored("b", 0.8)];

		var reranker = RelevanceModelReranker<NativeScale>.Create(new FakeRelevanceModel(0.5));

		await Assert.That(async () => await reranker.Run(pool)).Throws<InvalidOperationException>();
	}
}
