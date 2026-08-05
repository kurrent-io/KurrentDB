// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Pipeline;

[Category("Pipeline")]
public class KontextRetrieverTests {
	[Test]
	public async ValueTask cuts_min_score_and_limit_after_stages() {
		var retriever = KontextRetriever.New()
			.AddSearch(new FakeSearch(RetrievalSources.Vector,
				Fixtures.Candidate("a", 0.9),
				Fixtures.Candidate("b", 0.8),
				Fixtures.Candidate("c", 0.7),
				Fixtures.Candidate("d", 0.6)))
			.AddStage(new RescoringStage(scored => scored.Memory.MemoryId == "a" ? 0.1 : scored.Score))
			.Build();

		var result = await retriever.RetrieveAsync(new() { Text = "query", MinScore = 0.5, Limit = 2 });

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["b", "c"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask runs_stages_in_order() {
		var calls = new List<string>();

		var retriever = KontextRetriever.New()
			.AddSearch(new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9)))
			.AddStage(new RecordingStage("first", calls))
			.AddStage(new RecordingStage("second", calls))
			.Build();

		await retriever.RetrieveAsync(new() { Text = "query" });

		await Assert.That(calls).IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask defaults_to_rank_fusion_for_multiple_searches() {
		var retriever = KontextRetriever.New()
			.AddSearch(new FakeSearch(RetrievalSources.Vector,
				Fixtures.Candidate("a", 0.9),
				Fixtures.Candidate("b", 0.8)))
			.AddSearch(new FakeSearch(RetrievalSources.Keyword,
				Fixtures.Candidate("b", 12.0),
				Fixtures.Candidate("c", 8.0)))
			.Build();

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["b", "a", "c"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask failing_search_fails_retrieval() {
		var retriever = KontextRetriever.New()
			.AddSearch(new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9)))
			.AddSearch(new ThrowingSearch(RetrievalSources.Keyword))
			.Fuser(ReciprocalRankFuser.Create())
			.Build();

		await Assert.That(async () => await retriever.RetrieveAsync(new() { Text = "query" })).Throws<InvalidOperationException>();
	}

	[Test]
	public async ValueTask rejects_zero_searches() {
		var planner = new DefaultQueryPlanner(new OverfetchOptions(), TimeProvider.System);

		await Assert.That(() => new KontextRetriever(planner, [], new IdentityFuser(), [])).Throws<ArgumentException>();
	}
}
