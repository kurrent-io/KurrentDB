// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Composition;

[Category("Composition")]
public class NonFiniteScoreCompositionTests {
	[Test]
	public async ValueTask zeroed_weights_make_normalized_rank_fusion_produce_nan() {
		var fuser = ReciprocalRankFuser.Create(static options => {
			options.Normalize                         = true;
			options.Weights[RetrievalSources.Vector]  = 0.0;
			options.Weights[RetrievalSources.Keyword] = 0.0;
		});

		IReadOnlyList<CandidateSet> sets = [
			new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.9), Fixtures.Candidate("b", 0.8)]),
			new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("b", 12.0)]),
		];

		var pool = fuser.Fuse(sets, Fixtures.Query());

		// every weight zero ⇒ fused = 0 and maxScore = (0 + 0)/61 = 0, so the rescale is 0/0
		await Assert.That(double.IsNaN(pool[0].Score)).IsTrue();
		await Assert.That(double.IsNaN(pool[1].Score)).IsTrue();
		await Assert.That(double.IsNaN(pool[0].Breakdown.Fused)).IsTrue();
		await Assert.That(double.IsNaN(pool[1].Breakdown.Fused)).IsTrue();
	}

	[Test]
	public async ValueTask mmr_returns_every_member_of_a_non_finite_pool() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", double.NaN, "the quick brown fox jumps over the lazy dog"),
			Fixtures.Scored("a-dup", double.NaN, "the quick brown fox jumps over the lazy cat"),
			Fixtures.Scored("c", double.NaN, "completely different topic about databases entirely"),
		];

		var result = await MmrReorderer<NativeScale>.Create().Run(pool);

		// non-finite relevance drops to 0, so every first-step value ties at 0 and pool order wins;
		// from there diversity alone decides and a-dup, sharing 7 of 9 tokens with a, sinks last
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["a", "c", "a-dup"], CollectionOrdering.Matching);
		await Assert.That(result[0].Breakdown.ReorderScore!.Value).IsEqualTo(0.0).Within(1e-12);
		await Assert.That(result[1].Breakdown.ReorderScore!.Value).IsEqualTo(0.0).Within(1e-12);
		await Assert.That(result[2].Breakdown.ReorderScore!.Value).IsEqualTo(-(1 - 0.7) * (7.0 / 9.0)).Within(1e-12);
	}

	[Test]
	public async ValueTask nan_from_fusion_reaches_mmr_without_losing_a_memory() {
		var fuser = ReciprocalRankFuser.Create(static options => {
			options.Normalize                        = true;
			options.Weights[RetrievalSources.Vector] = 0.0;
		});

		IReadOnlyList<CandidateSet> sets = [
			new CandidateSet(RetrievalSources.Vector, [
				new SearchCandidate(Fixtures.Memory("a", "aardvarks burrow deep underground"), 0.9),
				new SearchCandidate(Fixtures.Memory("b", "penguins waddle across antarctic ice"), 0.8),
				new SearchCandidate(Fixtures.Memory("c", "giraffes browse the tallest acacia leaves"), 0.7),
			]),
		];

		var pool   = fuser.Fuse(sets, Fixtures.Query());
		var result = await MmrReorderer<NativeScale>.Create().Run(pool);

		await Assert.That(double.IsNaN(pool[0].Score)).IsTrue();
		await Assert.That(Fixtures.Ids(result).Order().ToList()).IsEquivalentTo(["a", "b", "c"], CollectionOrdering.Matching);
	}
}
