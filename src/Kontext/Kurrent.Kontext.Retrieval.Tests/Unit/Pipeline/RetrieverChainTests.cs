// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Pipeline;

[Category("Pipeline")]
public class RetrieverChainTests {
	[Test]
	public async ValueTask identity_fusion_passes_a_single_leg_through_untouched() {
		var retriever = Chains.Retriever(
			new IdentityFuser(),
			[new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("a", 12.0), Fixtures.Candidate("b", 5.0))]);

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		// the leg's native scores survive; rank fusion would have flattened them to 1/61 and 1/62
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["a", "b"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo(12.0);
		await Assert.That(result[1].Score).IsEqualTo(5.0);
		await Assert.That(result[0].Breakdown.SourceScores[RetrievalSources.Keyword]).IsEqualTo(12.0);
	}

	[Test]
	public async ValueTask unnormalized_rank_fusion_merges_two_legs() {
		var retriever = Chains.Retriever(
			ReciprocalRankFuser.Create(),
			[
				new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("both", 0.9), Fixtures.Candidate("v", 0.8)),
				new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("both", 12.0), Fixtures.Candidate("k", 5.0)),
			]);

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		// both = 1/61 + 1/61 raw — Normalize stays off, so a fused score never reaches 1.0;
		// k and v tie at 1/62 and break on memory id
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["both", "k", "v"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo(2.0 / 61).Within(1e-12);
		await Assert.That(result[1].Score).IsEqualTo(1.0 / 62).Within(1e-12);
		await Assert.That(result[0].Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(1);
		await Assert.That(result[0].Breakdown.SourceRanks[RetrievalSources.Keyword]).IsEqualTo(1);
	}

	[Test]
	public async ValueTask rank_fusion_over_a_single_leg_flattens_its_native_scores() {
		var retriever = Chains.Retriever(
			ReciprocalRankFuser.Create(),
			[new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("a", 12.0), Fixtures.Candidate("b", 5.0))]);

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		// 1/61 and 1/62, not the 12.0 and 5.0 an IdentityFuser would have passed through
		await Assert.That(result[0].Score).IsEqualTo(1.0 / 61).Within(1e-12);
		await Assert.That(result[1].Score).IsEqualTo(1.0 / 62).Within(1e-12);
	}

	[Test]
	public async ValueTask interleave_fusion_seats_each_legs_top_pick_first() {
		var retriever = Chains.Retriever(
			new InterleaveFuser(),
			[
				new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("x", 0.9), Fixtures.Candidate("both", 0.8)),
				new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("y", 12.0), Fixtures.Candidate("both", 5.0)),
			]);

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		// interleaving seats each leg's #1 first, scoring by position: x=3, y=2, both=1.
		// RRF would have topped the pool with both at 1/62 + 1/62.
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["x", "y", "both"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo(3.0);
	}

	[Test]
	public async ValueTask planner_override_shapes_the_query_the_searches_see() {
		var search  = new RecordingSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9));
		var planner = new DefaultQueryPlanner(new OverfetchOptions { Factor = 10, Floor = 1 }, new PinnedClock(Fixtures.Now));

		var retriever = Chains.Retriever(planner, new IdentityFuser(), [search]);

		await retriever.RetrieveAsync(new() { Text = "query", Limit = 7 });

		// 7 × 10 over a floor of 1, where the default policy would have planned max(7 × 4, 60) = 60
		await Assert.That(search.Planned!.PoolSize).IsEqualTo(70);
		await Assert.That(search.Planned!.AsOf).IsEqualTo(Fixtures.Now);
	}
}

sealed class RecordingSearch(string name, params SearchCandidate[] candidates) : ISearch {
	public string Name => name;

	public PlannedQuery? Planned { get; private set; }

	public ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
		Planned = query;

		return ValueTask.FromResult(new CandidateSet(name, candidates));
	}
}

sealed class PinnedClock(DateTimeOffset now) : TimeProvider {
	public override DateTimeOffset GetUtcNow() => now;
}
