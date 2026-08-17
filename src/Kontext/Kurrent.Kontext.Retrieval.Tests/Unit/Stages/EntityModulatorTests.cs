// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class EntityModulatorTests {
	static readonly PlannedQuery Question = Fixtures.Query("where does emily chen live");

	[Test]
	public async ValueTask an_absent_index_is_a_pass_through() {
		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("low", 0.1), Fixtures.Scored("high", 0.9)];

		var result = await EntityModulator<NativeScale>.Create(null).Run(pool, Question);

		// not even a re-sort: the incoming order survives a stage that had nothing to say
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["low", "high"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo(0.1);
		await Assert.That(result[1].Score).IsEqualTo(0.9);
	}

	[Test]
	public async ValueTask a_question_naming_nothing_known_leaves_the_pool_untouched() {
		var index = new FakeEntityIndex().Named("ada lovelace", "ent-ada").Mentions("ent-ada", "low");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("low", 0.1), Fixtures.Scored("high", 0.9)];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["low", "high"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo(0.1);
		await Assert.That(result[1].Score).IsEqualTo(0.9);
		await Assert.That(result.All(scored => scored.Breakdown.EntitySignal is null)).IsTrue();

		// the question was asked, the walks were not — nothing matched, so there was nothing to walk
		await Assert.That(index.MatchCalls).IsEqualTo(1);
		await Assert.That(index.NoteCalls).IsEqualTo(0);
		await Assert.That(index.MentionCalls).IsEqualTo(0);
	}

	[Test]
	public async ValueTask a_single_mention_is_the_sharpest_clue_and_takes_the_full_boost() {
		var index = new FakeEntityIndex().Named("emily chen", "ent-emily").Mentions("ent-emily", "named");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("named", 0.5), Fixtures.Scored("other", 0.5)];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		// rarity(1) = 1.00 → signal 1.00 → 1 + 0.1·1.00 = 1.10
		await Assert.That(byId["named"].Breakdown.EntitySignal!.Value).IsEqualTo(1.0).Within(1e-12);
		await Assert.That(byId["named"].Score).IsEqualTo(0.55).Within(1e-12);
		await Assert.That(byId["other"].Score).IsEqualTo(0.5);
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["named", "other"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask a_name_on_everything_is_no_clue_and_is_never_a_penalty() {
		// rarity(2000) = 1/(1 + 0.001·1999²) ≈ 0.00025. Matching on noise is worth nothing — and
		// nothing is where it stops: no reachable signal can push a memory below where it arrived.
		var index = new FakeEntityIndex().Named("emily chen", "ent-everyone", mentionCount: 2000).Mentions("ent-everyone", "common");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("common", 0.5), Fixtures.Scored("other", 0.5)];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		await Assert.That(byId["common"].Breakdown.EntitySignal!.Value).IsLessThan(0.001);
		await Assert.That(byId["common"].Score).IsGreaterThanOrEqualTo(0.5);
		await Assert.That(byId["common"].Score).IsEqualTo(0.5).Within(1e-3);
		await Assert.That(byId["other"].Score).IsEqualTo(0.5);
	}

	[Test]
	public async ValueTask a_counter_of_zero_reads_as_maximally_rare() {
		// A hand-taught entity, or one whose last mention was swept: the curve never wraps past 1.
		var index = new FakeEntityIndex().Named("emily chen", "ent-taught", mentionCount: 0).Mentions("ent-taught", "taught");

		var result = await EntityModulator<NativeScale>.Create(index).Run([Fixtures.Scored("taught", 0.5)], Question);

		await Assert.That(result[0].Breakdown.EntitySignal!.Value).IsEqualTo(1.0).Within(1e-12);
	}

	[Test]
	public async ValueTask crossing_a_note_reaches_the_neighbour_at_the_penalized_weight() {
		var index = new FakeEntityIndex()
			.Named("emily chen", "ent-emily")
			.Note("ent-emily", "ent-emilia", 0.84)
			.Mentions("ent-emily", "emily")
			.Mentions("ent-emilia", "emilia");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("emily", 0.5), Fixtures.Scored("emilia", 0.5), Fixtures.Scored("other", 0.5)];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		// 1.00 rarity × 0.84 note × 0.5 penalty = 0.42, the design's own worked example
		await Assert.That(byId["emilia"].Breakdown.EntitySignal!.Value).IsEqualTo(0.42).Within(1e-12);
		await Assert.That(byId["emily"].Breakdown.EntitySignal!.Value).IsEqualTo(1.0).Within(1e-12);

		// 1 + 0.1·0.42 = 1.042: a real push at a fraction of the named entity's, so the doubt
		// reaches the reader and still loses to any better match
		await Assert.That(byId["emilia"].Score).IsEqualTo(0.5 * 1.042).Within(1e-12);
		await Assert.That(byId["emily"].Score).IsEqualTo(0.55).Within(1e-12);
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["emily", "emilia", "other"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask a_note_crosses_in_either_direction() {
		// Stored source→target order is storage order, not meaning: the named entity is the TARGET here.
		var index = new FakeEntityIndex()
			.Named("emily chen", "ent-emily")
			.Note("ent-emilia", "ent-emily", 0.84)
			.Mentions("ent-emilia", "emilia");

		var result = await EntityModulator<NativeScale>.Create(index).Run([Fixtures.Scored("emilia", 0.5)], Question);

		await Assert.That(result[0].Breakdown.EntitySignal!.Value).IsEqualTo(0.42).Within(1e-12);
	}

	[Test]
	public async ValueTask a_second_hop_contributes_nothing() {
		var index = new FakeEntityIndex()
			.Named("emily chen", "ent-emily")
			.Note("ent-emily", "ent-emilia", 0.84)
			.Note("ent-emilia", "ent-far", 0.84)
			.Mentions("ent-emilia", "one-hop")
			.Mentions("ent-far", "two-hops");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("one-hop", 0.5), Fixtures.Scored("two-hops", 0.5)];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		await Assert.That(byId["one-hop"].Breakdown.EntitySignal!.Value).IsEqualTo(0.42).Within(1e-12);
		await Assert.That(byId["two-hops"].Breakdown.EntitySignal).IsNull();
		await Assert.That(byId["two-hops"].Score).IsEqualTo(0.5);

		// the neighbourhood is asked for ONCE, off the named entity — never again off a neighbour
		await Assert.That(index.NoteCalls).IsEqualTo(1);
	}

	[Test]
	public async ValueTask clues_compound_with_diminishing_returns() {
		var index = new FakeEntityIndex()
			.Named("emily chen", "ent-emily")
			.Note("ent-emily", "ent-a", 0.6)
			.Note("ent-emily", "ent-b", 0.8)
			.Mentions("ent-a", "both")
			.Mentions("ent-b", "both");

		var result = await EntityModulator<NativeScale>.Create(index).Run([Fixtures.Scored("both", 0.5)], Question);

		// weights 0.3 and 0.4 → 1 − (1 − 0.3)(1 − 0.4) = 0.58, under the 0.7 a plain sum would give
		await Assert.That(result[0].Breakdown.EntitySignal!.Value).IsEqualTo(0.58).Within(1e-12);
		await Assert.That(result[0].Score).IsEqualTo(0.5 * 1.058).Within(1e-12);
	}

	[Test]
	public async ValueTask one_entity_mentioned_twice_in_a_memory_is_one_clue() {
		// Provenance is append-only and positional, so a name at two offsets files two rows.
		var index = new FakeEntityIndex()
			.Named("emily chen", "ent-emily")
			.Note("ent-emily", "ent-emilia", 0.84)
			.Mentions("ent-emilia", "emilia", "emilia");

		var result = await EntityModulator<NativeScale>.Create(index).Run([Fixtures.Scored("emilia", 0.5)], Question);

		// 0.42, not the 1 − 0.58² = 0.6636 a double count would compound to
		await Assert.That(result[0].Breakdown.EntitySignal!.Value).IsEqualTo(0.42).Within(1e-12);
	}

	[Test]
	public async ValueTask unreached_memories_keep_their_score_and_relative_order() {
		var index = new FakeEntityIndex().Named("emily chen", "ent-emily").Mentions("ent-emily", "c");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("a", 0.5), Fixtures.Scored("b", 0.5), Fixtures.Scored("c", 0.5)];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["c", "a", "b"], CollectionOrdering.Matching);
		await Assert.That(result[1].Score).IsEqualTo(0.5);
		await Assert.That(result[2].Score).IsEqualTo(0.5);
		await Assert.That(result[1].Breakdown.EntitySignal).IsNull();
		await Assert.That(result[2].Breakdown.EntitySignal).IsNull();
	}

	[Test]
	public async ValueTask a_match_that_reaches_no_candidate_leaves_the_pool_untouched() {
		var index = new FakeEntityIndex().Named("emily chen", "ent-emily").Mentions("ent-emily", "not-in-pool");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("low", 0.1), Fixtures.Scored("high", 0.9)];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["low", "high"], CollectionOrdering.Matching);
		await Assert.That(result[0].Score).IsEqualTo(0.1);
	}

	[Test]
	public async ValueTask an_entity_too_common_to_matter_is_never_walked() {
		// rarity(100_000) ≈ 1e-7 cannot move any multiplier, and its mention list is the longest in
		// the store — the walk is skipped rather than paid for.
		var index = new FakeEntityIndex().Named("emily chen", "ent-everyone", mentionCount: 100_000).Mentions("ent-everyone", "common");

		var result = await EntityModulator<NativeScale>.Create(index).Run([Fixtures.Scored("common", 0.5)], Question);

		await Assert.That(result[0].Score).IsEqualTo(0.5);
		await Assert.That(result[0].Breakdown.EntitySignal).IsNull();
		await Assert.That(index.MentionCalls).IsEqualTo(0);
	}

	[Test]
	public async ValueTask the_ceiling_clamps_when_the_gain_is_retuned_up() {
		var index = new FakeEntityIndex()
			.Named("emily chen", "ent-emily")
			.Note("ent-emily", "ent-emilia", 0.84)
			.Mentions("ent-emily", "named")
			.Mentions("ent-emilia", "hopped");

		IReadOnlyList<ScoredMemory> pool = [Fixtures.Scored("named", 0.5), Fixtures.Scored("hopped", 0.5)];

		// α = 2.0 would take signal 1.00 to ×3.00 and signal 0.42 to ×1.84; the rail refuses both, so
		// relevance stays king however the gain is tuned
		var result = await EntityModulator<NativeScale>.Create(index, options => options.SignalAlpha = 2.0).Run(pool, Question);
		var byId   = result.ToDictionary(scored => scored.Memory.MemoryId);

		await Assert.That(byId["named"].Score).IsEqualTo(0.5 * 1.10).Within(1e-12);
		await Assert.That(byId["hopped"].Score).IsEqualTo(0.5 * 1.10).Within(1e-12);
	}

	[Test]
	public async ValueTask no_reachable_signal_can_demote_a_memory() {
		// The floor at 1.00 is the rail behind it: an entity tie is evidence FOR relevance, so a
		// sharp clue, a worthless one, and a barely-believed note all come back at or above where
		// they arrived — only the size of the push varies.
		var index = new FakeEntityIndex()
			.Named("emily chen", "ent-rare")
			.Named("chen", "ent-common", mentionCount: 900)
			.Note("ent-rare", "ent-doubted", 0.05)
			.Mentions("ent-rare", "rare")
			.Mentions("ent-common", "common")
			.Mentions("ent-doubted", "doubted");

		IReadOnlyList<ScoredMemory> pool = [
			Fixtures.Scored("rare", 0.5),
			Fixtures.Scored("common", 0.5),
			Fixtures.Scored("doubted", 0.5),
			Fixtures.Scored("untouched", 0.5),
		];

		var result = await EntityModulator<NativeScale>.Create(index).Run(pool, Question);

		await Assert.That(result.Where(scored => scored.Score < 0.5).ToList()).IsEmpty();
		await Assert.That(Fixtures.Ids(result)[0]).IsEqualTo("rare");
	}
}
