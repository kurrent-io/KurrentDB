// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Pipeline;

[Category("Pipeline")]
public class KontextRetrieverTests {
	[Test]
	public async ValueTask cuts_min_score_and_limit_after_stages() {
		var retriever = Chains.Retriever(
			new IdentityFuser(),
			[
				new FakeSearch(RetrievalSources.Vector,
					Fixtures.Candidate("a", 0.9),
					Fixtures.Candidate("b", 0.8),
					Fixtures.Candidate("c", 0.7),
					Fixtures.Candidate("d", 0.6)),
			],
			new RescoringStage(scored => scored.Memory.MemoryId == "a" ? 0.1 : scored.Score));

		var result = await retriever.RetrieveAsync(new() { Text = "query", MinScore = 0.5, Limit = 2 });

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["b", "c"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask runs_stages_in_composition_order() {
		var calls = new List<string>();

		var retriever = Chains.Retriever(
			new IdentityFuser(),
			[new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9))],
			new RecordingStage("first", calls),
			new RecordingStage("second", calls));

		await retriever.RetrieveAsync(new() { Text = "query" });

		await Assert.That(calls).IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask rank_fusion_merges_multiple_searches() {
		var retriever = Chains.Retriever(
			ReciprocalRankFuser.Create(),
			[
				new FakeSearch(RetrievalSources.Vector,
					Fixtures.Candidate("a", 0.9),
					Fixtures.Candidate("b", 0.8)),
				new FakeSearch(RetrievalSources.Keyword,
					Fixtures.Candidate("b", 12.0),
					Fixtures.Candidate("c", 8.0)),
			]);

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["b", "a", "c"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask failing_search_fails_retrieval() {
		var retriever = Chains.Retriever(
			ReciprocalRankFuser.Create(),
			[
				new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9)),
				new ThrowingSearch(RetrievalSources.Keyword),
			]);

		await Assert.That(async () => await retriever.RetrieveAsync(new() { Text = "query" })).Throws<InvalidOperationException>();
	}

	[Test]
	public async ValueTask rejects_zero_searches() {
		await Assert.That(() => new SearchStep()).Throws<ArgumentException>();
	}
}
