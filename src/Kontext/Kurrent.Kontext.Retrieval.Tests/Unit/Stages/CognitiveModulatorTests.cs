// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class CognitiveModulatorTests {

	[Test]
	public async ValueTask breakdown_reproduces_score() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.9, importance: MemoryContracts.MemoryImportance.Critical, age: TimeSpan.Zero),
			Fixtures.Scored("b", 0.5, importance: MemoryContracts.MemoryImportance.Normal, age: TimeSpan.FromDays(7)),
			Fixtures.Scored("c", 0.2, importance: MemoryContracts.MemoryImportance.Low, age: TimeSpan.FromDays(30)),
		];

		var result = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), pool);

		foreach (var scored in result) {
			var breakdown = scored.Breakdown;

			var expectedBase = 0.05 * breakdown.RecencyNorm!.Value
			                 + 0.20 * breakdown.ImportanceNorm!.Value
			                 + 0.75 * breakdown.RelevanceNorm!.Value;

			await Assert.That(breakdown.BaseScore!.Value).IsEqualTo(expectedBase).Within(1e-12);
			await Assert.That(scored.Score).IsEqualTo(breakdown.BaseScore.Value).Within(1e-12);
		}
	}
}
