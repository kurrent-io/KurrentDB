// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Composition;

// A MinScore threshold is only meaningful against the scale the chain ends on — visible here in
// the types: the raw chain ends on Pool<RrfScale> and cuts with CutStep<RrfScale>, the modulated
// chain ends on Pool<UnitScale>. The same 0.1 cutoff empties one and keeps the other whole.
[Category("Composition")]
public class MinScoreScaleCompositionTests {
	static IStep<RetrievalQuery, Pool<RrfScale>> Legs() =>
		PlanStep.Default()
			.Then(new SearchStep(
				new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.9), Fixtures.Candidate("b", 0.8)),
				new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("a", 12.0), Fixtures.Candidate("b", 10.0))))
			.Then(new FuseStep<RrfScale>(ReciprocalRankFuser.Create()));

	[Test]
	public async ValueTask the_same_min_score_empties_raw_rank_fusion_and_keeps_a_modulated_pool() {
		var raw = KontextRetriever.From("raw-rrf", Legs().Then(new CutStep<RrfScale>()));

		// a raw RRF pool tops out at 1/61 + 1/61 ≈ 0.0328, so a 0.1 cutoff takes the whole pool
		await Assert.That(await raw.RetrieveAsync(new() { Text = "query", AsOf = Fixtures.Now, MinScore = 0.1 })).IsEmpty();

		var kept = await raw.RetrieveAsync(new() { Text = "query", AsOf = Fixtures.Now });

		// the same pipeline with no cutoff ranks both — it is the scale that dropped them, not the pipeline
		await Assert.That(Fixtures.Ids(kept)).IsEquivalentTo(["a", "b"], CollectionOrdering.Matching);
		await Assert.That(kept[0].Score).IsEqualTo(2.0 / 61).Within(1e-12);
		await Assert.That(kept[1].Score).IsEqualTo(2.0 / 62).Within(1e-12);

		var modulated = KontextRetriever.From("modulated",
			Legs()
				.Then(CognitiveModulator<RrfScale>.Create())
				.Then(new CutStep<UnitScale>()));

		var result = await modulated.RetrieveAsync(new() { Text = "query", AsOf = Fixtures.Now, MinScore = 0.1 });

		// identical age and importance pin recency and importance at the neutral 0.5; relevance
		// min-maxes to 1 and 0, certainty is Fact 0.9 — so even the loser clears 0.1 comfortably
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["a", "b"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo((0.05 * 0.5 + 0.2 * 0.5 + 0.75 * 1.0) * 0.9).Within(1e-12);
		await Assert.That(result[1].Score).IsEqualTo((0.05 * 0.5 + 0.2 * 0.5 + 0.75 * 0.0) * 0.9).Within(1e-12);
	}
}
