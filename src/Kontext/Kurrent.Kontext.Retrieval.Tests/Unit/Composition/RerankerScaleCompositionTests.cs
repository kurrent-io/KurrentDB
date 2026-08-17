// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Composition;

// RelevanceModelReranker replaces head scores with model relevance in [0,1] and concatenates a tail
// still carrying raw fused RRF scores (~0.016–0.033). Nothing reconciles the two scales, so the next
// stage that treats Score as one axis — CognitiveModulator's relevance min-max — reads them together.
[Category("Composition")]
public class RerankerScaleCompositionTests {
	static readonly Contracts.StoredMemory Alpha   = Fixtures.Memory("a", "alpha passage about vector indexes");
	static readonly Contracts.StoredMemory Bravo   = Fixtures.Memory("b", "bravo passage about query planning");
	static readonly Contracts.StoredMemory Charlie = Fixtures.Memory("c", "charlie passage about rank fusion");

	static KontextRetriever Pipeline(IRelevanceModel model) =>
		KontextRetriever.From("reranked-modulated",
			PlanStep.Default()
				.Then(new SearchStep(
					new FakeSearch(RetrievalSources.Vector,
						new SearchCandidate(Alpha, 0.9),
						new SearchCandidate(Bravo, 0.8),
						new SearchCandidate(Charlie, 0.7)),
					new FakeSearch(RetrievalSources.Keyword,
						new SearchCandidate(Alpha, 12.0),
						new SearchCandidate(Bravo, 10.0),
						new SearchCandidate(Charlie, 9.0))))
				.Then(new FuseStep<RrfScale>(ReciprocalRankFuser.Create()))
				.Then(RelevanceModelReranker<RrfScale>.Create(model, static options => options.CandidateCap = 2))
				.Then(CognitiveModulator<NativeScale>.Create())
				.Then(new CutStep<UnitScale>()));

	[Test]
	public async ValueTask an_unjudged_tail_min_maxes_to_zero_relevance_below_a_well_rated_head() {
		var model = new FakeRelevanceModel(new Dictionary<string, double> {
			[Alpha.Content]   = 0.10,
			[Bravo.Content]   = 0.20,
			[Charlie.Content] = 0.99,
		});

		var result = await Pipeline(model).RetrieveAsync(new() { Text = "query", AsOf = Fixtures.Now });
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		// the cap stops at the top two, so charlie never reaches the model — the 0.99 it would have
		// scored, the best of the three, is never asked for
		await Assert.That(model.Calls).IsEqualTo(1);
		await Assert.That(model.LastPassages).IsEquivalentTo([Alpha.Content, Bravo.Content], CollectionOrdering.Matching);
		await Assert.That(byId["c"].Breakdown.Reranked).IsNull();

		// charlie carries its raw RRF score (rank 3 in both legs = 2/63) onto the same axis as the
		// head's model relevance, so the min-max hands it exactly zero
		await Assert.That(byId["c"].Breakdown.RelevanceRaw!.Value).IsEqualTo(2.0 / 63).Within(1e-12);
		await Assert.That(byId["c"].Breakdown.RelevanceNorm!.Value).IsEqualTo(0.0).Within(1e-12);

		// identical age and importance pin those dimensions at the neutral 0.5; certainty is Fact 0.9
		await Assert.That(byId["c"].Score).IsEqualTo((0.05 * 0.5 + 0.2 * 0.5 + 0.75 * 0.0) * 0.9).Within(1e-12);
		await Assert.That(byId["b"].Score).IsEqualTo((0.05 * 0.5 + 0.2 * 0.5 + 0.75 * 1.0) * 0.9).Within(1e-12);
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["b", "a", "c"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask a_poorly_rated_head_lets_the_unjudged_tail_win_the_relevance_axis() {
		// both model scores land BELOW the tail's untouched 2/63 ≈ 0.0317
		var model = new FakeRelevanceModel(new Dictionary<string, double> {
			[Alpha.Content] = 0.02,
			[Bravo.Content] = 0.01,
		});

		IReadOnlyList<CandidateSet> sets = [
			new CandidateSet(RetrievalSources.Vector, [new SearchCandidate(Alpha, 0.9), new SearchCandidate(Bravo, 0.8), new SearchCandidate(Charlie, 0.7)]),
			new CandidateSet(RetrievalSources.Keyword, [new SearchCandidate(Alpha, 12.0), new SearchCandidate(Bravo, 10.0), new SearchCandidate(Charlie, 9.0)]),
		];

		var pool     = ReciprocalRankFuser.Create().Fuse(sets, Fixtures.Query());
		var reranked = await RelevanceModelReranker<RrfScale>.Create(model, static options => options.CandidateCap = 2).Run(pool);

		// the concat puts the tail last by construction, so the pool leaves the stage NOT in
		// descending score order — 0.02, 0.01, 0.0317
		await Assert.That(Fixtures.Ids(reranked)).IsEquivalentTo(["a", "b", "c"], CollectionOrdering.Matching);
		await Assert.That(reranked[0].Score).IsEqualTo(0.02).Within(1e-12);
		await Assert.That(reranked[1].Score).IsEqualTo(0.01).Within(1e-12);
		await Assert.That(reranked[2].Score).IsEqualTo(2.0 / 63).Within(1e-12);

		var result = await Pipeline(model).RetrieveAsync(new() { Text = "query", AsOf = Fixtures.Now });

		// and once a modulator reads that axis, the memory the model never saw takes first place
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["c", "a", "b"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo((0.05 * 0.5 + 0.2 * 0.5 + 0.75 * 1.0) * 0.9).Within(1e-12);
		await Assert.That(result[1].Score).IsEqualTo((0.05 * 0.5 + 0.2 * 0.5 + 0.75 * ((0.02 - 0.01) / (2.0 / 63 - 0.01))) * 0.9).Within(1e-12);
		await Assert.That(result[2].Score).IsEqualTo((0.05 * 0.5 + 0.2 * 0.5 + 0.75 * 0.0) * 0.9).Within(1e-12);
	}
}
