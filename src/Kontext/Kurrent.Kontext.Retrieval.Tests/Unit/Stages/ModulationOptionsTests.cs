// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class ModulationOptionsTests {
	[Test]
	public async ValueTask partial_importance_weights_do_not_throw_for_a_present_key() {
		// the buggy GetValueOrDefault(importance, ImportanceWeights[Normal]) evaluates its fallback
		// argument eagerly, so omitting Normal threw a KeyNotFoundException even though Critical,
		// the key actually requested, was present
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.5, importance: Contracts.MemoryImportance.Critical),
		];

		var options = CognitiveModulator.Create(o => o.ImportanceWeights = new() {
			[Contracts.MemoryImportance.Critical] = 1.0,
		});

		var result = await options.ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result[0].Breakdown.ImportanceRaw!.Value).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask empty_importance_weights_falls_back_to_neutral_salience() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.5, importance: Contracts.MemoryImportance.Normal),
		];

		var options = CognitiveModulator.Create(o => o.ImportanceWeights = new());

		var result = await options.ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result[0].Breakdown.ImportanceRaw!.Value).IsEqualTo(0.5);
	}

	[Test]
	public async ValueTask unknown_importance_value_falls_back_to_neutral_salience() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.5, importance: (Contracts.MemoryImportance) 999),
		];

		var options = CognitiveModulator.Create(o => o.ImportanceWeights = new() {
			[Contracts.MemoryImportance.Normal] = 0.5,
		});

		var result = await options.ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result[0].Breakdown.ImportanceRaw!.Value).IsEqualTo(0.5);
	}

}
