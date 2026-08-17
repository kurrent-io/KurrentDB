// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Composition;

[Category("Composition")]
public class FuserSourceCompatibilityCompositionTests {
	[Test]
	public async ValueTask additive_fusion_rejects_a_hybrid_leg_at_the_pipeline_level() {
		var retriever = Chains.Retriever(
			AdditiveNormalizedFuser.Create(),
			[
				new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("v", 0.9)),
				new FakeSearch(RetrievalSources.Hybrid, Fixtures.Candidate("h", 0.8)),
			]);

		// additive fusion only calibrates vector and keyword; an engine-hybrid leg has no sigmoid
		await Assert.That(async () => await retriever.RetrieveAsync(new() { Text = "query" })).Throws<NotSupportedException>();
	}

	[Test]
	public async ValueTask additive_fusion_accepts_the_two_legs_it_calibrates() {
		var retriever = Chains.Retriever(
			AdditiveNormalizedFuser.Create(static options => {
				options.Midpoint  = 5.0;
				options.Steepness = 0.7;
			}),
			[
				new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("a", 0.8)),
				new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("a", 5.0)),
			]);

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		// (0.8 + sigmoid(5 at midpoint 5) = 0.5) / 2 active signals — the same shape that throws above
		await Assert.That(result[0].Score).IsEqualTo((0.8 + 0.5) / 2).Within(1e-12);
	}

	[Test]
	public async ValueTask identity_fusion_rejects_a_second_leg_at_the_pipeline_level() {
		var retriever = Chains.Retriever(
			new IdentityFuser(),
			[
				new FakeSearch(RetrievalSources.Vector, Fixtures.Candidate("v", 0.9)),
				new FakeSearch(RetrievalSources.Keyword, Fixtures.Candidate("k", 12.0)),
			]);

		await Assert.That(async () => await retriever.RetrieveAsync(new() { Text = "query" })).Throws<InvalidOperationException>();
	}

	[Test]
	public async ValueTask a_lone_hybrid_leg_passes_its_own_scores_through_identity_fusion() {
		var retriever = Chains.Retriever(
			new IdentityFuser(),
			[new FakeSearch(RetrievalSources.Hybrid, Fixtures.Candidate("h", 0.8), Fixtures.Candidate("i", 0.6))]);

		var result = await retriever.RetrieveAsync(new() { Text = "query" });

		// a single-leg chain fuses with IdentityFuser, so hybrid is only a problem once a fuser
		// that calibrates per-source sees it
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["h", "i"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo(0.8);
	}
}
