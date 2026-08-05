// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class MmrReordererTests {
	[Test]
	public async ValueTask demotes_near_duplicates() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 1.0, "the quick brown fox jumps over the lazy dog"),
			Fixtures.Scored("a-dup", 0.95, "the quick brown fox jumps over the lazy cat"),
			Fixtures.Scored("c", 0.9, "completely different topic about databases entirely"),
			Fixtures.Scored("d", 0.1, "unrelated filler entry mentioning penguins"),
		];

		var result = await MmrReorderer.Create().ProcessAsync(Fixtures.Query(), pool);

		// a-dup shares 7 of 9 tokens with a, so the diverse c leapfrogs it; scores stay untouched
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["a", "c", "a-dup", "d"], CollectionOrdering.Matching);
		await Assert.That(result.Single(scored => scored.Memory.MemoryId == "a-dup").Score).IsEqualTo(0.95);
	}

	[Test]
	public async ValueTask lambda_one_degrades_to_a_plain_relevance_resort() {
		// near-identical contents (only the last word differs) so a diversity term, if it were
		// active, would visibly demote the near-duplicates; λ=1 zeroes that term entirely
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("s1", 1.0, "the quick brown fox jumps over the lazy dog"),
			Fixtures.Scored("s2", 0.8, "the quick brown fox jumps over the lazy cat"),
			Fixtures.Scored("s3", 0.5, "the quick brown fox jumps over the lazy bat"),
			Fixtures.Scored("s4", 0.2, "the quick brown fox jumps over the lazy rat"),
		];

		var result = await MmrReorderer.Create(options => options.Lambda = 1).ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["s1", "s2", "s3", "s4"], CollectionOrdering.Matching);

		// with (1-λ)=0 the picked value is exactly the candidate's normalized relevance every step;
		// min=0.2, max=1.0, range=0.8, and the top member normalizes to exactly 1.0
		await Assert.That(result[0].Breakdown.ReorderScore!.Value).IsEqualTo((1.0 - 0.2) / 0.8).Within(1e-12);
		await Assert.That(result[1].Breakdown.ReorderScore!.Value).IsEqualTo((0.8 - 0.2) / 0.8).Within(1e-12);
		await Assert.That(result[2].Breakdown.ReorderScore!.Value).IsEqualTo((0.5 - 0.2) / 0.8).Within(1e-12);
		await Assert.That(result[3].Breakdown.ReorderScore!.Value).IsEqualTo((0.2 - 0.2) / 0.8).Within(1e-12);
	}

	[Test]
	public async ValueTask lambda_zero_ignores_relevance_first_pick_is_pool_order() {
		// scores are set to actively contradict pool order (p1 has the highest score, p0 the
		// lowest) so a relevance-driven pick would disagree with what actually gets picked first
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("p0", 0.2, "alpha beta gamma delta"),
			Fixtures.Scored("p1", 0.9, "alpha beta gamma delta"),
			Fixtures.Scored("p2", 0.5, "epsilon zeta eta theta"),
			Fixtures.Scored("p3", 0.6, "iota kappa lambda mu"),
		];

		var result = await MmrReorderer.Create(options => options.Lambda = 0).ProcessAsync(Fixtures.Query(), pool);

		// λ=0 zeroes the relevance term, and maxSimToSelected starts at 0 for everyone, so every
		// candidate's value is 0 on step one; the loop only replaces bestIndex on a strict `>`,
		// so the first (index 0) candidate wins the tie regardless of its score — p0, not p1
		// p1 is a byte-identical duplicate of p0 (Jaccard 1.0), so it is maximally penalized and
		// picked last; p2 and p3 are mutually disjoint from p0 and each other (Jaccard 0), so they
		// tie at value 0 and break on index order: p2 before p3
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["p0", "p2", "p3", "p1"], CollectionOrdering.Matching);
		await Assert.That(result[0].Breakdown.ReorderScore!.Value).IsEqualTo(0.0);
		await Assert.That(result[1].Breakdown.ReorderScore!.Value).IsEqualTo(0.0);
		await Assert.That(result[2].Breakdown.ReorderScore!.Value).IsEqualTo(0.0);
		await Assert.That(result[3].Breakdown.ReorderScore!.Value).IsEqualTo(-1.0);
	}

	[Test]
	public async ValueTask lowering_lambda_lets_a_diverse_candidate_leapfrog() {
		// same pool as demotes_near_duplicates: a-dup shares 7 of 9 Jaccard tokens with a, c is
		// disjoint from both; rel(a)=1, rel(a-dup)=17/18, rel(c)=8/9 (min=0.1, max=1.0, range=0.9)
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 1.0, "the quick brown fox jumps over the lazy dog"),
			Fixtures.Scored("a-dup", 0.95, "the quick brown fox jumps over the lazy cat"),
			Fixtures.Scored("c", 0.9, "completely different topic about databases entirely"),
			Fixtures.Scored("d", 0.1, "unrelated filler entry mentioning penguins"),
		];

		// at λ=0.95 the diversity penalty on a-dup (0.05 * 7/9 ≈ 0.039) is too small to close the
		// relevance gap to c (17/18 ≈ .944 vs 8/9 ≈ .889): a-dup stays put right after a
		var high = await MmrReorderer.Create(options => options.Lambda = 0.95).ProcessAsync(Fixtures.Query(), pool);
		await Assert.That(Fixtures.Ids(high)).IsEquivalentTo(["a", "a-dup", "c", "d"], CollectionOrdering.Matching);

		// at λ=0.7 the same penalty (0.3 * 7/9 ≈ .233) is large enough: c leapfrogs a-dup
		var low = await MmrReorderer.Create(options => options.Lambda = 0.7).ProcessAsync(Fixtures.Query(), pool);
		await Assert.That(Fixtures.Ids(low)).IsEquivalentTo(["a", "c", "a-dup", "d"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask every_input_score_survives_reordering_unchanged() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.95, "aardvarks burrow deep underground"),
			Fixtures.Scored("b", 0.7, "aardvarks dig deep underground burrows"),
			Fixtures.Scored("c", 0.4, "penguins waddle across antarctic ice"),
			Fixtures.Scored("d", 0.4, "giraffes browse the tallest acacia leaves"),
			Fixtures.Scored("e", 0.05, "completely unrelated filler text"),
		];

		var scoresById = pool.ToDictionary(scored => scored.Memory.MemoryId, scored => scored.Score);
		var result     = await MmrReorderer.Create().ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result.Count).IsEqualTo(pool.Count);

		foreach (var scored in result)
			await Assert.That(scored.Score).IsEqualTo(scoresById[scored.Memory.MemoryId]);
	}

	[Test]
	public async ValueTask custom_similarity_changes_the_order_the_jaccard_default_would_keep() {
		// single-character contents tokenize to nothing (Jaccard's tokenizer drops length-1 tokens),
		// so the default Similarity is always 0 here and this pool degrades to plain relevance order
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("A", 1.0, "A"),
			Fixtures.Scored("B", 0.9, "B"),
			Fixtures.Scored("C", 0.5, "C"),
		];

		var byDefault = await MmrReorderer.Create(options => options.Lambda = 0.5).ProcessAsync(Fixtures.Query(), pool);
		await Assert.That(Fixtures.Ids(byDefault)).IsEquivalentTo(["A", "B", "C"], CollectionOrdering.Matching);

		var calls = 0;

		double CountingLookup(string left, string right) {
			calls++;
			return left is "A" or "B" && right is "A" or "B" && left != right ? 1.0 : 0.0;
		}

		// a custom similarity that treats A and B as full duplicates (everything else as unrelated):
		// rel(A)=1, rel(B)=0.8, rel(C)=0 (min=0.5, max=1.0, range=0.5); A is picked first as before,
		// but B's diversity penalty (0.5 * 1.0 = 0.5) now drags it below C (0.5*0.8-0.5=-0.1 < 0=C),
		// so C leapfrogs B — a reordering the inert Jaccard default could never produce on this pool
		var withCustom = await MmrReorderer.Create(options => {
			options.Lambda     = 0.5;
			options.Similarity = CountingLookup;
		}).ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(Fixtures.Ids(withCustom)).IsEquivalentTo(["A", "C", "B"], CollectionOrdering.Matching);

		// 3 candidates: (n-1) + (n-2) + ... + 0 = 2 + 1 + 0 = 3 similarity calls total
		await Assert.That(calls).IsEqualTo(3);
	}

	[Test]
	public async ValueTask similarity_calls_scale_quadratically_not_cubically() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("1", 0.9, "content one"),
			Fixtures.Scored("2", 0.8, "content two"),
			Fixtures.Scored("3", 0.7, "content three"),
			Fixtures.Scored("4", 0.6, "content four"),
			Fixtures.Scored("5", 0.5, "content five"),
		];

		var calls = 0;

		double Counting(string left, string right) {
			calls++;
			return ScoreNormalization.JaccardSimilarity(left, right);
		}

		await MmrReorderer.Create(options => options.Similarity = Counting).ProcessAsync(Fixtures.Query(), pool);

		// the running maxSimToSelected is updated once per pick against every still-unpicked
		// candidate, so each of the n picks costs (n - step - 1) calls: (n-1) + (n-2) + ... + 0
		// = n(n-1)/2 = 5*4/2 = 10 — not the O(n^3) a naive rescan of the selected list would cost
		await Assert.That(calls).IsEqualTo(5 * 4 / 2);
	}

	[Test]
	public async ValueTask degenerate_relevance_pool_orders_purely_by_diversity() {
		// identical scores make min == max, so MinMax normalizes every relevance to the neutral
		// 0.5 (ScoreNormalization.MinMax's degenerate branch); with relevance constant across the
		// pool, λ*0.5 is the same additive constant for every candidate at every step, so the
		// argmax comparison collapses to picking whoever currently has the smallest maxSimToSelected
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("e0", 0.42, "alpha beta gamma delta"),
			Fixtures.Scored("e1", 0.42, "alpha beta gamma delta"),
			Fixtures.Scored("e2", 0.42, "epsilon zeta eta theta"),
			Fixtures.Scored("e3", 0.42, "iota kappa lambda mu"),
		];

		var result = await MmrReorderer.Create().ProcessAsync(Fixtures.Query(), pool);

		// e0 wins the opening all-zero tie on index order; e1 is its byte-identical duplicate
		// (Jaccard 1.0) so it is maximally penalized and picked last; e2 and e3 are disjoint from
		// e0 and each other, tying at the smallest possible penalty and breaking on index order
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["e0", "e2", "e3", "e1"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask two_element_pool_has_no_alternative_candidate_to_promote() {
		// even near-duplicate content can't be reordered when there's only one other candidate:
		// the second pick is forced, so a 2-element pool always comes back in relevance order
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("x", 0.9, "the quick brown fox jumps over the lazy dog"),
			Fixtures.Scored("y", 0.85, "the quick brown fox jumps over the lazy cat"),
		];

		var result = await MmrReorderer.Create().ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["x", "y"], CollectionOrdering.Matching);

		// min=0.85, max=0.9, range=0.05: rel(x)=1.0, rel(y)=0.0
		await Assert.That(result[0].Breakdown.ReorderScore!.Value).IsEqualTo(0.7 * 1.0).Within(1e-12);

		// y is forced regardless of its value: 0.7*0 - 0.3*(7/9), the full diversity penalty against
		// x, landing below zero even though nothing was ever available to out-compete it
		await Assert.That(result[1].Breakdown.ReorderScore!.Value).IsEqualTo(0.7 * 0.0 - 0.3 * (7.0 / 9.0)).Within(1e-12);
	}

	[Test]
	public async ValueTask byte_identical_contents_collapse_the_pool_to_relevance_order() {
		// every content is the exact same string, so Jaccard similarity between any two of them is
		// exactly 1.0; after the first pick, every remaining candidate's maxSimToSelected saturates
		// to 1.0 and stays there, making the diversity term a constant per-step penalty — so, just
		// like λ=1, the pool degrades to plain descending relevance order, but by a different route
		const string content = "the quick brown fox jumps over the lazy dog";

		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("i1", 1.0, content),
			Fixtures.Scored("i2", 0.7, content),
			Fixtures.Scored("i3", 0.4, content),
			Fixtures.Scored("i4", 0.1, content),
		];

		var result = await MmrReorderer.Create().ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["i1", "i2", "i3", "i4"], CollectionOrdering.Matching);

		// min=0.1, max=1.0, range=0.9: rel(i1)=1.0, rel(i2)=2/3, rel(i3)=1/3, rel(i4)=0
		await Assert.That(result[0].Breakdown.ReorderScore!.Value).IsEqualTo(0.7 * 1.0).Within(1e-12);
		await Assert.That(result[1].Breakdown.ReorderScore!.Value).IsEqualTo(0.7 * (0.6 / 0.9) - 0.3).Within(1e-12);
		await Assert.That(result[2].Breakdown.ReorderScore!.Value).IsEqualTo(0.7 * (0.3 / 0.9) - 0.3).Within(1e-12);
		await Assert.That(result[3].Breakdown.ReorderScore!.Value).IsEqualTo(0.7 * 0.0 - 0.3).Within(1e-12);
	}
}
