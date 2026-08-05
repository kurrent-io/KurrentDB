// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class CognitiveModulatorTests {
	[Test]
	public async ValueTask derives_certainty_from_citations() {
		// identical age, importance, and relevance leave every normalized dimension at the neutral
		// 0.5, so base = 0.5 and final = 0.5 × certainty — the certainty rules decide alone
		var synth = Fixtures.Scored("synth", 0.5, type: Contracts.MemoryType.Summary, cites: ["obs", "gossip", "missing"]);
		synth.Memory.Evidence.Add(new Contracts.Evidence {
			Record = new Contracts.Evidence.Types.RecordRef { Id = "record-1" },
		});

		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("obs", 0.5, type: Contracts.MemoryType.Observation),
			Fixtures.Scored("gossip", 0.5, type: Contracts.MemoryType.Hearsay),
			synth,
		];

		var result = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), pool);
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		// synth = mean(obs 1.0, gossip 0.25, unresolved 0.5, record 0.9) = 0.6625
		await Assert.That(byId["obs"].Breakdown.Certainty).IsEqualTo(1.0);
		await Assert.That(byId["gossip"].Breakdown.Certainty).IsEqualTo(0.25);
		await Assert.That(byId["synth"].Breakdown.Certainty!.Value).IsEqualTo(0.6625).Within(1e-12);
		await Assert.That(byId["synth"].Score).IsEqualTo(0.5 * 0.6625).Within(1e-12);
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["obs", "synth", "gossip"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask breakdown_reproduces_score() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.9, importance: Contracts.MemoryImportance.Critical, age: TimeSpan.Zero),
			Fixtures.Scored("b", 0.5, importance: Contracts.MemoryImportance.Normal, age: TimeSpan.FromDays(7)),
			Fixtures.Scored("c", 0.2, importance: Contracts.MemoryImportance.Low, age: TimeSpan.FromDays(30)),
		];

		var result = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), pool);

		foreach (var scored in result) {
			var breakdown = scored.Breakdown;

			var expectedBase = 0.05 * breakdown.RecencyNorm!.Value
			                 + 0.20 * breakdown.ImportanceNorm!.Value
			                 + 0.75 * breakdown.RelevanceNorm!.Value;

			await Assert.That(breakdown.BaseScore!.Value).IsEqualTo(expectedBase).Within(1e-12);
			await Assert.That(scored.Score).IsEqualTo(breakdown.BaseScore.Value * breakdown.Certainty!.Value).Within(1e-12);
		}
	}
}
