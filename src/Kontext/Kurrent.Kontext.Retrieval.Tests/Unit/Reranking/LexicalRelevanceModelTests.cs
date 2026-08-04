// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Reranking;

[Category("Reranking")]
public class LexicalRelevanceModelTests {
	[Test]
	public async ValueTask scores_each_passage_by_query_overlap_in_input_order() {
		var scores = await new LexicalRelevanceModel().ScoreAsync("vector search latency", [
			"unrelated prose here",
			"latency of vector search",
			"vector search",
			"vector search latency",
		]);

		// query holds 3 tokens; each score is shared / (3 + passage tokens − shared): 0/6, 3/4, 2/3, 3/3
		await Assert.That(scores[0]).IsEqualTo(0.0);
		await Assert.That(scores[1]).IsEqualTo(3.0 / 4).Within(1e-12);
		await Assert.That(scores[2]).IsEqualTo(2.0 / 3).Within(1e-12);
		await Assert.That(scores[3]).IsEqualTo(1.0).Within(1e-12);
	}

	[Test]
	public async ValueTask single_character_query_shares_nothing_with_anything() {
		// the tokenizer keeps only tokens longer than one character, so a one-letter query has no
		// tokens to share and every passage scores 0 — including a passage identical to the query
		var scores = await new LexicalRelevanceModel().ScoreAsync("a", ["a", "a vector"]);

		await Assert.That(scores[0]).IsEqualTo(0.0);
		await Assert.That(scores[1]).IsEqualTo(0.0);
	}

	[Test]
	public async ValueTask empty_passage_list_scores_nothing() {
		var scores = await new LexicalRelevanceModel().ScoreAsync("vector search latency", []);

		await Assert.That(scores).IsEmpty();
	}

	[Test]
	public async ValueTask repeated_calls_score_identically() {
		var model = new LexicalRelevanceModel();

		IReadOnlyList<string> passages = ["latency of vector search", "unrelated prose here"];

		var first  = await model.ScoreAsync("vector search latency", passages);
		var second = await model.ScoreAsync("vector search latency", passages);

		await Assert.That(second.ToList()).IsEquivalentTo(first.ToList(), CollectionOrdering.Matching);
	}
}
