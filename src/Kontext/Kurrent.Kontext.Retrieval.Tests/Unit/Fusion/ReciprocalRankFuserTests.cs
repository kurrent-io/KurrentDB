// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fusion;

[Category("Fusion")]
public class ReciprocalRankFuserTests {
	[Test]
	public async ValueTask fuses_ranks_across_legs() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("both", 0.9), Fixtures.Candidate("zz", 0.8)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("both", 12.0), Fixtures.Candidate("aa", 5.0)]);

		var pool = ReciprocalRankFuser.Create().Fuse([vector, keyword], Fixtures.Query());

		// both = 1/61 + 1/61; aa and zz tie at 1/62 and break on memory id
		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["both", "aa", "zz"], CollectionOrdering.Matching);
		await Assert.That(pool[0].Score).IsEqualTo(2.0 / 61).Within(1e-12);
		await Assert.That(pool[0].Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(1);
		await Assert.That(pool[0].Breakdown.SourceRanks[RetrievalSources.Keyword]).IsEqualTo(1);
		await Assert.That(pool[0].Breakdown.SourceScores[RetrievalSources.Vector]).IsEqualTo(0.9);
		await Assert.That(pool[0].Breakdown.SourceScores[RetrievalSources.Keyword]).IsEqualTo(12.0);
	}

	[Test]
	public async ValueTask normalize_rescales_against_the_all_legs_maximum() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("both", 0.9), Fixtures.Candidate("zz", 0.8)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("both", 12.0), Fixtures.Candidate("aa", 5.0)]);

		var pool = ReciprocalRankFuser.Create(static options => options.Normalize = true).Fuse([vector, keyword], Fixtures.Query());

		await Assert.That(pool[0].Score).IsEqualTo(1.0).Within(1e-12);
		await Assert.That(pool[0].Breakdown.Fused).IsEqualTo(1.0).Within(1e-12);
		await Assert.That(pool[1].Score).IsEqualTo((1.0 / 62) * 61 / 2).Within(1e-12);
	}

	[Test]
	public async ValueTask weights_scale_leg_contributions() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("v", 0.9)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 9.0)]);

		var fuser = ReciprocalRankFuser.Create(options => options.Weights[RetrievalSources.Keyword] = 2.0);
		var pool  = fuser.Fuse([vector, keyword], Fixtures.Query());

		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["k", "v"], CollectionOrdering.Matching);
		await Assert.That(pool[0].Score).IsEqualTo(2.0 / 61).Within(1e-12);
		await Assert.That(pool[1].Score).IsEqualTo(1.0 / 61).Within(1e-12);
	}
}
