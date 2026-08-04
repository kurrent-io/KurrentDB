// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fusion;

[Category("Fusion")]
public class IdentityFuserTests {
	[Test]
	public async ValueTask single_set_passes_through_with_score_order_and_provenance_preserved() {
		var vector = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.7), Fixtures.Candidate("b", 0.3)]);

		var pool = new IdentityFuser().Fuse([vector], Fixtures.Query());

		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["a", "b"], CollectionOrdering.Matching);
		await Assert.That(pool[0].Score).IsEqualTo(0.7).Within(1e-12);
		await Assert.That(pool[0].Breakdown.Fused).IsEqualTo(0.7).Within(1e-12);
		await Assert.That(pool[0].Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(1);
		await Assert.That(pool[0].Breakdown.SourceScores[RetrievalSources.Vector]).IsEqualTo(0.7);
		await Assert.That(pool[1].Score).IsEqualTo(0.3).Within(1e-12);
		await Assert.That(pool[1].Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(2);
	}

	[Test]
	public async ValueTask throws_on_zero_sets() {
		IReadOnlyList<CandidateSet> sets = [];

		await Assert.That(() => new IdentityFuser().Fuse(sets, Fixtures.Query()))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("got 0");
	}

	[Test]
	public async ValueTask throws_on_two_or_more_sets() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.7)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("b", 0.3)]);

		await Assert.That(() => new IdentityFuser().Fuse([vector, keyword], Fixtures.Query()))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("got 2");
	}

	[Test]
	public async ValueTask a_set_with_zero_candidates_returns_an_empty_pool_without_throwing() {
		var vector = new CandidateSet(RetrievalSources.Vector, []);

		var pool = new IdentityFuser().Fuse([vector], Fixtures.Query());

		await Assert.That(pool).IsEmpty();
	}
}
