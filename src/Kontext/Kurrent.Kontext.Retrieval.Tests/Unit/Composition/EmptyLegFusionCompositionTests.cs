// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Composition;

// The two fusers disagree about a leg that RAN and returned nothing. AdditiveNormalizedFuser flips
// anyVector/anyKeyword inside the per-candidate callback, so an empty leg never counts and the
// surviving leg keeps its full magnitude. ReciprocalRankFuser.Normalize sums the weight of EVERY
// set into maxScore, so an empty leg depresses every normalized score. Same pipeline shape, opposite
// convention — pinned here as-is, not fixed: which one is right is the owner's call.
[Category("Composition")]
public class EmptyLegFusionCompositionTests {
	[Test]
	public async ValueTask additive_fusion_ignores_a_leg_that_returned_nothing() {
		var fuser = AdditiveNormalizedFuser.Create(static options => {
			options.Midpoint  = 5.0;
			options.Steepness = 0.7;
		});

		IReadOnlyList<CandidateSet> withEmptyKeyword = [
			new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.8)]),
			new CandidateSet(RetrievalSources.Keyword, []),
		];

		// activeSignals stays 1, so the vector score is not halved: 0.8, not 0.8/2
		await Assert.That(fuser.Fuse(withEmptyKeyword, Fixtures.Query())[0].Score).IsEqualTo(0.8).Within(1e-12);

		IReadOnlyList<CandidateSet> vectorOnly = [new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.8)])];

		await Assert.That(fuser.Fuse(vectorOnly, Fixtures.Query())[0].Score).IsEqualTo(0.8).Within(1e-12);

		IReadOnlyList<CandidateSet> bothPopulated = [
			new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.8)]),
			new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("a", 5.0)]),
		];

		// one keyword hit at the sigmoid midpoint is enough to halve the vector contribution
		await Assert.That(fuser.Fuse(bothPopulated, Fixtures.Query())[0].Score).IsEqualTo((0.8 + 0.5) / 2).Within(1e-12);
	}

	[Test]
	public async ValueTask normalized_rank_fusion_charges_for_a_leg_that_returned_nothing() {
		var fuser = ReciprocalRankFuser.Create(static options => options.Normalize = true);

		IReadOnlyList<CandidateSet> withEmptyKeyword = [
			new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.9)]),
			new CandidateSet(RetrievalSources.Keyword, []),
		];

		// maxScore sums the weight of every set: (1 + 1)/61, so the top vector hit is (1/61)/(2/61)
		await Assert.That(fuser.Fuse(withEmptyKeyword, Fixtures.Query())[0].Score).IsEqualTo(0.5).Within(1e-12);

		IReadOnlyList<CandidateSet> vectorOnly = [new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.9)])];

		// dropping the empty set instead of passing it doubles the very same memory's score
		await Assert.That(fuser.Fuse(vectorOnly, Fixtures.Query())[0].Score).IsEqualTo(1.0).Within(1e-12);
	}

	[Test]
	public async ValueTask additive_fusion_returns_an_empty_pool_when_every_leg_is_empty() {
		var fuser = AdditiveNormalizedFuser.Create();

		IReadOnlyList<CandidateSet> nothing = [
			new CandidateSet(RetrievalSources.Vector, []),
			new CandidateSet(RetrievalSources.Keyword, []),
		];

		// activeSignals is 0 and the division is skipped entirely — no NaN escapes
		await Assert.That(fuser.Fuse(nothing, Fixtures.Query())).IsEmpty();
	}
}
