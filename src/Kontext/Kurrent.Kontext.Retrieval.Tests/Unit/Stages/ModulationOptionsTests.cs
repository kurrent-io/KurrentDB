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

	[Test]
	public async ValueTask partial_certainty_weights_do_not_throw_for_a_present_key() {
		// same eager-evaluation hazard as importance: CertaintyWeights[Unspecified] being absent
		// must not throw while resolving a memory typed Observation, which IS present
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.5, type: Contracts.MemoryType.Observation),
		];

		var options = CognitiveModulator.Create(o => o.CertaintyWeights = new() {
			[Contracts.MemoryType.Observation] = 1.0,
		});

		var result = await options.ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result[0].Breakdown.Certainty!.Value).IsEqualTo(1.0);
	}

	[Test]
	public async ValueTask empty_certainty_weights_falls_back_to_neutral_certainty() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.5, type: Contracts.MemoryType.Fact),
		];

		var options = CognitiveModulator.Create(o => o.CertaintyWeights = new());

		var result = await options.ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result[0].Breakdown.Certainty!.Value).IsEqualTo(0.5);
	}

	[Test]
	public async ValueTask unknown_memory_type_falls_back_to_neutral_certainty() {
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("a", 0.5, type: (Contracts.MemoryType) 999),
		];

		var options = CognitiveModulator.Create(o => o.CertaintyWeights = new() {
			[Contracts.MemoryType.Unspecified] = 0.5,
		});

		var result = await options.ProcessAsync(Fixtures.Query(), pool);

		await Assert.That(result[0].Breakdown.Certainty!.Value).IsEqualTo(0.5);
	}

	// --- Pool-relative certainty: CognitiveModulator.CertaintyOf resolves a derived memory's
	// citations against the CANDIDATE POOL only, one hop, no store round-trip. The following pin
	// that as intentional, documented behavior rather than a bug: the same derived memory can
	// legitimately score differently depending on which other memories the query happened to
	// retrieve alongside it.

	[Test]
	public async ValueTask derived_certainty_depends_on_whether_the_cited_memory_is_in_the_pool() {
		var citingObs = Fixtures.Scored("synth", 0.5, type: Contracts.MemoryType.Summary, cites: ["obs"]);

		IReadOnlyList<ScoredMemory> poolWithCitedMemory = [
			Fixtures.Scored("obs", 0.5, type: Contracts.MemoryType.Observation),
			citingObs,
		];

		IReadOnlyList<ScoredMemory> poolWithoutCitedMemory = [
			Fixtures.Scored("filler", 0.5, type: Contracts.MemoryType.Fact),
			citingObs,
		];

		var present = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), poolWithCitedMemory);
		var absent  = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), poolWithoutCitedMemory);

		// "obs" resolved: certainty = mean(Observation 1.0) = 1.0
		await Assert.That(present.Single(scored => scored.Memory.MemoryId == "synth").Breakdown.Certainty!.Value).IsEqualTo(1.0);

		// "obs" unresolvable in this pool: certainty = mean(UnresolvedCitationCertainty 0.5) = 0.5
		await Assert.That(absent.Single(scored => scored.Memory.MemoryId == "synth").Breakdown.Certainty!.Value).IsEqualTo(0.5);
	}

	[Test]
	public async ValueTask record_citation_certainty_ignores_pool_composition() {
		var citingRecord = Fixtures.Scored("synth", 0.5, type: Contracts.MemoryType.Summary);
		citingRecord.Memory.Evidence = new Contracts.Evidence();
		citingRecord.Memory.Evidence.Citations.Add(new Contracts.Evidence.Types.Citation {
			Record = new Contracts.Evidence.Types.RecordRef { Id = "record-1" },
		});

		IReadOnlyList<ScoredMemory> alone = [citingRecord];

		IReadOnlyList<ScoredMemory> withCompany = [
			citingRecord,
			Fixtures.Scored("obs", 0.5, type: Contracts.MemoryType.Observation),
		];

		var resultAlone      = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), alone);
		var resultWithCompany = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), withCompany);

		// RecordCitationCertainty = 0.9, regardless of what else is (or isn't) in the pool
		await Assert.That(resultAlone.Single(scored => scored.Memory.MemoryId == "synth").Breakdown.Certainty!.Value).IsEqualTo(0.9);
		await Assert.That(resultWithCompany.Single(scored => scored.Memory.MemoryId == "synth").Breakdown.Certainty!.Value).IsEqualTo(0.9);
	}

	[Test]
	public async ValueTask mixed_resolvable_and_unresolvable_citations_average_correctly() {
		var synth = Fixtures.Scored("synth", 0.5, type: Contracts.MemoryType.Summary, cites: ["obs", "gossip", "gone"]);

		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("obs", 0.5, type: Contracts.MemoryType.Observation),
			Fixtures.Scored("gossip", 0.5, type: Contracts.MemoryType.Hearsay),
			synth,
		];

		var result = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), pool);

		// mean(obs 1.0, gossip 0.25, unresolved 0.5) = 1.75 / 3
		await Assert.That(result.Single(scored => scored.Memory.MemoryId == "synth").Breakdown.Certainty!.Value)
			.IsEqualTo((1.0 + 0.25 + 0.5) / 3).Within(1e-12);
	}

	[Test]
	public async ValueTask citing_a_derived_memory_takes_its_type_weight_not_its_computed_certainty() {
		// innerCited is itself derived (cites gossip, a Hearsay), so ITS OWN computed certainty is
		// 0.25 — but outer, which cites innerCited, must land on CertaintyWeights[Summary] = 0.80,
		// the flat type weight, never the 0.25 innerCited actually earned. Trust does not compound.
		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("gossip", 0.5, type: Contracts.MemoryType.Hearsay),
			Fixtures.Scored("innerCited", 0.5, type: Contracts.MemoryType.Summary, cites: ["gossip"]),
			Fixtures.Scored("outer", 0.5, type: Contracts.MemoryType.Summary, cites: ["innerCited"]),
		];

		var result = await CognitiveModulator.Create().ProcessAsync(Fixtures.Query(), pool);
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		await Assert.That(byId["innerCited"].Breakdown.Certainty!.Value).IsEqualTo(0.25);
		await Assert.That(byId["outer"].Breakdown.Certainty!.Value).IsEqualTo(0.80).Within(1e-12);
	}
}
