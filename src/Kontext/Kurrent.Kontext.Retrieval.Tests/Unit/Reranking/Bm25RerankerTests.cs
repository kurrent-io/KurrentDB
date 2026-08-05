// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Reranking;

[Category("Reranking")]
public class Bm25RerankerTests {
	[Test]
	public async ValueTask defaults_pin_the_measured_winning_configuration() {
		var options = new Bm25RerankerOptions();

		await Assert.That(options.K).IsEqualTo(10);
		await Assert.That(options.B).IsEqualTo(0);
		await Assert.That(options.IdentityWeight).IsEqualTo(1);
		await Assert.That(options.Bm25Weight).IsEqualTo(2);
	}

	[Test]
	public async ValueTask the_reread_alone_puts_the_only_matching_candidate_first() {
		// Arrange — the query term appears in exactly one candidate, buried last by the incoming order.
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.9, content: "weather was lovely on sunday"),
			Fixtures.Scored("b", 0.8, content: "the meeting ran very long"),
			Fixtures.Scored("c", 0.7, content: "we had a picnic by the lake"),
		];

		var reranker = Bm25Reranker.Create(static options => options.IdentityWeight = 0);

		// Act
		var result = await reranker.ProcessAsync(Fixtures.Query("picnic"), pool);

		// Assert
		await Assert.That(result[0].Memory.MemoryId).IsEqualTo("c");
	}

	[Test]
	public async ValueTask zero_bm25_weight_preserves_the_incoming_order() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.9, content: "nothing relevant here"),
			Fixtures.Scored("b", 0.8, content: "a picnic by the lake"),
			Fixtures.Scored("c", 0.7, content: "another picnic story"),
		];

		var reranker = Bm25Reranker.Create(static options => options.Bm25Weight = 0);

		var result = await reranker.ProcessAsync(Fixtures.Query("picnic"), pool);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["a", "b", "c"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask zero_b_stops_penalizing_long_candidates() {
		// Arrange — both carry the term once; only length differs. With b > 0 BM25 favors the short
		// one; with b = 0 their scores tie and the incoming order stands.
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("long", 0.9, content: "picnic with the whole family down by the lake with sandwiches lemonade and endless stories"),
			Fixtures.Scored("short", 0.8, content: "picnic today"),
		];

		var penalizing = await Bm25Reranker.Create(static options => {
			options.B              = 0.75;
			options.IdentityWeight = 0;
		}).ProcessAsync(Fixtures.Query("picnic"), pool);

		var flat = await Bm25Reranker.Create(static options => options.IdentityWeight = 0)
			.ProcessAsync(Fixtures.Query("picnic"), pool);

		// Assert
		await Assert.That(penalizing[0].Memory.MemoryId).IsEqualTo("short");
		await Assert.That(flat[0].Memory.MemoryId).IsEqualTo("long");
	}

	[Test]
	public async ValueTask breakdown_records_the_pool_local_bm25_score() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("hit", 0.9, content: "a picnic by the lake"),
			Fixtures.Scored("miss", 0.8, content: "nothing relevant here"),
		];

		var result = await Bm25Reranker.Create().ProcessAsync(Fixtures.Query("picnic"), pool);

		var byId = result.ToDictionary(scored => scored.Memory.MemoryId);

		await Assert.That(byId["hit"].Breakdown.Reranked!.Value).IsGreaterThan(0);
		await Assert.That(byId["miss"].Breakdown.Reranked!.Value).IsEqualTo(0);
	}

	[Test]
	public async ValueTask a_single_candidate_pool_passes_through_untouched() {
		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("only", 0.9)];

		var result = await Bm25Reranker.Create().ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result).IsEquivalentTo(pool, CollectionOrdering.Matching);
	}
}
