// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Retrieval.Tests.Composition;

[Category("Composition")]
public class ProvenanceSurvivalCompositionTests {
	static readonly MemoryContracts.StoredMemory Alpha =
		Fixtures.Memory("a", "aardvarks burrow deep underground", importance: MemoryContracts.MemoryImportance.Critical);

	static readonly MemoryContracts.StoredMemory Bravo =
		Fixtures.Memory("b", "penguins waddle across antarctic ice", importance: MemoryContracts.MemoryImportance.Low);

	[Test]
	public async ValueTask fusion_provenance_outlives_modulation_and_reordering() {
		var retriever = KontextRetriever.New()
			.AddSearch(new FakeSearch(RetrievalSources.Vector, new SearchCandidate(Alpha, 0.9), new SearchCandidate(Bravo, 0.8)))
			.AddSearch(new FakeSearch(RetrievalSources.Keyword, new SearchCandidate(Bravo, 12.0), new SearchCandidate(Alpha, 5.0)))
			.Fuser(ReciprocalRankFuser.Create())
			.AddStage(CognitiveModulator.Create())
			.AddStage(MmrReorderer.Create())
			.Build();

		var result = await retriever.RetrieveAsync(new() { Text = "query", AsOf = Fixtures.Now });
		var top    = result[0];

		// a and b swap ranks across the legs, so both fuse to 1/61 + 1/62 and importance breaks the tie
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["a", "b"], CollectionOrdering.Matching);

		await Assert.That(top.Breakdown.Fused).IsEqualTo(1.0 / 61 + 1.0 / 62).Within(1e-12);
		await Assert.That(top.Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(1);
		await Assert.That(top.Breakdown.SourceRanks[RetrievalSources.Keyword]).IsEqualTo(2);
		await Assert.That(top.Breakdown.SourceScores[RetrievalSources.Vector]).IsEqualTo(0.9);
		await Assert.That(top.Breakdown.SourceScores[RetrievalSources.Keyword]).IsEqualTo(5.0);

		// equal fused scores leave relevance degenerate at 0.5, as does equal age; Critical vs Low
		// min-maxes importance to 1 and 0, and certainty is Fact 0.9
		await Assert.That(top.Breakdown.RelevanceRaw!.Value).IsEqualTo(1.0 / 61 + 1.0 / 62).Within(1e-12);
		await Assert.That(top.Breakdown.RelevanceNorm!.Value).IsEqualTo(0.5).Within(1e-12);
		await Assert.That(top.Breakdown.RecencyNorm!.Value).IsEqualTo(0.5).Within(1e-12);
		await Assert.That(top.Breakdown.ImportanceRaw!.Value).IsEqualTo(1.0).Within(1e-12);
		await Assert.That(top.Breakdown.ImportanceNorm!.Value).IsEqualTo(1.0).Within(1e-12);
		await Assert.That(top.Breakdown.Certainty).IsEqualTo(0.9);
		await Assert.That(top.Breakdown.BaseScore!.Value).IsEqualTo(0.2 * 0.5 + 0.2 * 1.0 + 0.6 * 0.5).Within(1e-12);
		await Assert.That(top.Score).IsEqualTo((0.2 * 0.5 + 0.2 * 1.0 + 0.6 * 0.5) * 0.9).Within(1e-12);

		// the reorder adds its own number without erasing anything above it, and no stage invented a
		// Reranked value for a pipeline that ran no relevance model
		await Assert.That(top.Breakdown.ReorderScore!.Value).IsEqualTo(0.7).Within(1e-12);
		await Assert.That(top.Breakdown.Reranked).IsNull();

		var runnerUp = result[1];

		await Assert.That(runnerUp.Breakdown.Fused).IsEqualTo(1.0 / 62 + 1.0 / 61).Within(1e-12);
		await Assert.That(runnerUp.Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(2);
		await Assert.That(runnerUp.Breakdown.SourceRanks[RetrievalSources.Keyword]).IsEqualTo(1);
		await Assert.That(runnerUp.Breakdown.SourceScores[RetrievalSources.Vector]).IsEqualTo(0.8);
		await Assert.That(runnerUp.Breakdown.SourceScores[RetrievalSources.Keyword]).IsEqualTo(12.0);
		await Assert.That(runnerUp.Score).IsEqualTo((0.2 * 0.5 + 0.2 * 0.0 + 0.6 * 0.5) * 0.9).Within(1e-12);

		// b shares no tokens with a, so the diversity penalty is zero and its MMR value is 0.7·0
		await Assert.That(runnerUp.Breakdown.ReorderScore!.Value).IsEqualTo(0.0).Within(1e-12);
	}
}
