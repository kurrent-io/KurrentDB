// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fusion;

[Category("Fusion")]
public class InterleaveFuserTests {
	[Test]
	public async ValueTask interleaves_round_robin_across_two_sources_with_no_overlap() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("v1", 0.9), Fixtures.Candidate("v2", 0.5)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k1", 8.0), Fixtures.Candidate("k2", 3.0)]);

		var pool = new InterleaveFuser().Fuse([vector, keyword], Fixtures.Query());

		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["v1", "k1", "v2", "k2"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask ragged_source_keeps_contributing_after_the_shallower_source_is_exhausted() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("v1", 0.9), Fixtures.Candidate("v2", 0.7), Fixtures.Candidate("v3", 0.5)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k1", 8.0)]);

		var pool = new InterleaveFuser().Fuse([vector, keyword], Fixtures.Query());

		// depth 0: v1, k1 — depth 1: v2, keyword exhausted — depth 2: v3, keyword still exhausted
		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["v1", "k1", "v2", "v3"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask dedup_keeps_the_earlier_slot_and_drops_the_later_duplicate() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("shared", 0.9), Fixtures.Candidate("v2", 0.6)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k1", 8.0), Fixtures.Candidate("shared", 5.0)]);

		var pool = new InterleaveFuser().Fuse([vector, keyword], Fixtures.Query());

		// depth 0: shared (from vector) claims the slot, k1 — depth 1: v2, keyword's shared is already seen and dropped
		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["shared", "k1", "v2"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask interleave_order_survives_the_fused_score_resort() {
		// memory ids picked so the memory-id tiebreak, alone, would produce a different order (aa, bb, yy, zz)
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("zz", 0.9), Fixtures.Candidate("yy", 0.7)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("aa", 8.0), Fixtures.Candidate("bb", 3.0)]);

		var pool = new InterleaveFuser().Fuse([vector, keyword], Fixtures.Query());

		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["zz", "aa", "yy", "bb"], CollectionOrdering.Matching);

		// Fused = ordered.Count - position: 4, 3, 2, 1 — strictly decreasing, so the resort can't reshuffle it
		await Assert.That(pool[0].Breakdown.Fused).IsEqualTo(4.0).Within(1e-12);
		await Assert.That(pool[1].Breakdown.Fused).IsEqualTo(3.0).Within(1e-12);
		await Assert.That(pool[2].Breakdown.Fused).IsEqualTo(2.0).Within(1e-12);
		await Assert.That(pool[3].Breakdown.Fused).IsEqualTo(1.0).Within(1e-12);
	}

	[Test]
	public async ValueTask records_provenance_for_every_source_that_surfaced_a_shared_memory() {
		var vector  = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("shared", 0.9)]);
		var keyword = new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("shared", 5.0)]);

		var pool = new InterleaveFuser().Fuse([vector, keyword], Fixtures.Query());

		// the no-op accumulate callback never sets provenance, but FusionAccumulator.Collect
		// records rank/score for every set it walks regardless of interleave dedup
		await Assert.That(pool[0].Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(1);
		await Assert.That(pool[0].Breakdown.SourceRanks[RetrievalSources.Keyword]).IsEqualTo(1);
		await Assert.That(pool[0].Breakdown.SourceScores[RetrievalSources.Vector]).IsEqualTo(0.9);
		await Assert.That(pool[0].Breakdown.SourceScores[RetrievalSources.Keyword]).IsEqualTo(5.0);
	}

	[Test]
	public async ValueTask empty_set_list_produces_an_empty_pool() {
		IReadOnlyList<CandidateSet> sets = [];

		var pool = new InterleaveFuser().Fuse(sets, Fixtures.Query());

		await Assert.That(pool).IsEmpty();
	}

	[Test]
	public async ValueTask sets_with_zero_candidates_produce_an_empty_pool() {
		IReadOnlyList<CandidateSet> sets = [
			new CandidateSet(RetrievalSources.Vector, []),
			new CandidateSet(RetrievalSources.Keyword, []),
		];

		var pool = new InterleaveFuser().Fuse(sets, Fixtures.Query());

		await Assert.That(pool).IsEmpty();
	}

	[Test]
	public async ValueTask single_source_degrades_to_that_sources_own_order() {
		var vector = new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("v1", 0.9), Fixtures.Candidate("v2", 0.6), Fixtures.Candidate("v3", 0.3)]);

		var pool = new InterleaveFuser().Fuse([vector], Fixtures.Query());

		await Assert.That(Fixtures.Ids(pool)).IsEquivalentTo(["v1", "v2", "v3"], CollectionOrdering.Matching);
	}
}
