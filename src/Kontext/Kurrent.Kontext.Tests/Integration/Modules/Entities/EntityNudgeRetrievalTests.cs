// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Pipelines;
using Kurrent.Kontext.Retrieval;

namespace Kurrent.Kontext.Tests.Integration.Modules.Entities;

/// <summary>
/// The read path's entity stages end to end over a REAL DuckDB + Lance store: a question names one
/// entity, recognition matches it against stored surface forms, a pending note reaches its suspected
/// duplicate, and the nudge prices both. The stage math is pinned by
/// <c>Kurrent.Kontext.Retrieval.Tests</c>; what this suite owns is that the SQL, the normalization
/// rule, and the wiring actually meet.
/// <para>Keyword leg only, no model: the entity signal is the thing under test, and identical
/// content across every seed leaves relevance, recency, importance, and certainty uniform — so the
/// pool arrives at the nudge dead flat and the nudge alone moves it.</para>
/// </summary>
[Category("Integration")]
[Category("Entities")]
public class EntityNudgeRetrievalTests {
	static readonly DateTimeOffset Base = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

	const string Question = "where does emily chen live";
	const string Content  = "emily chen live somewhere on record";

	[Test]
	public async ValueTask a_named_entity_pushes_its_memories_and_a_note_reaches_the_neighbours() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var memories = await MemorySeeding.Seed(dataSources,
			Memory("mem-emily"),
			Memory("mem-emilia"),
			Memory("mem-unnamed"));

		await EntitySeeding.CreateSchema(dataSources);

		// "Emilia Chen" once scored 0.84 against Emily: too close to ignore, not close enough to
		// merge, so both entries live and a pending note joins them. The question names only Emily.
		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-emily", "Emily Chen", "PERSON", Base),
			new EntitySeed("ent-emilia", "Emilia Chen", "PERSON", Base));

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-emily", "mem-emily", "Emily Chen", Base),
			new MentionSeed("ent-emilia", "mem-emilia", "Emilia Chen", Base));

		EntitySeeding.Insert(dataSources, new LinkSeed("ent-emilia", "ent-emily", Base) { Confidence = 0.84 });

		var entities = new KontextEntityIndex(new KontextEntityStore(dataSources));

		// Act
		var flat   = await Retrieve(Chain(memories, null));
		var nudged = await Retrieve(Chain(memories, entities));

		// Assert
		var before = Scores(flat);
		var after  = Scores(nudged);

		// the pool really does arrive flat, so every difference below is the nudge's
		await Assert.That(before.Count).IsEqualTo(3);
		await Assert.That(before.Values.Distinct().Count()).IsEqualTo(1);

		var flatScore = before["mem-unnamed"];

		// no entity reached it, so signal 0 → multiplier exactly 1: unaffected, to the bit
		await Assert.That(after["mem-unnamed"]).IsEqualTo(flatScore);

		// rarity(1) = 1.00 → signal 1.00 → ×1.10
		await Assert.That(after["mem-emily"]).IsEqualTo(flatScore * 1.10).Within(1e-12);

		// one hop: 1.00 rarity × 0.84 note × 0.5 penalty = 0.42 → ×1.042. A real push, a fraction of
		// the named entity's — Emilia's memories reach the reader, and lose to Emily's.
		await Assert.That(after["mem-emilia"]).IsEqualTo(flatScore * 1.042).Within(1e-12);
		await Assert.That(after["mem-emilia"]).IsLessThan(after["mem-emily"]);
		await Assert.That(after["mem-emilia"]).IsGreaterThan(after["mem-unnamed"]);

		await Assert.That(Ids(nudged)).IsEquivalentTo(["mem-emily", "mem-emilia", "mem-unnamed"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask recognition_matches_a_stored_nickname_not_only_the_canonical_name() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var memories = await MemorySeeding.Seed(dataSources, Memory("mem-doctor"), Memory("mem-unnamed"));

		await EntitySeeding.CreateSchema(dataSources);

		// The canonical name normalizes to "dr. emily chen", which the question does NOT contain:
		// only the nickname can carry this match.
		EntitySeeding.Insert(dataSources, new EntitySeed("ent-emily", "Dr. Emily Chen", "PERSON", Base) {
			Aliases = ["dr. emily chen", "emily chen"],
		});

		EntitySeeding.Insert(dataSources, new MentionSeed("ent-emily", "mem-doctor", "Dr. Emily Chen", Base));

		var entities = new KontextEntityIndex(new KontextEntityStore(dataSources));

		// Act
		var flat   = await Retrieve(Chain(memories, null));
		var nudged = await Retrieve(Chain(memories, entities));

		// Assert
		var flatScore = Scores(flat)["mem-unnamed"];
		var after     = Scores(nudged);

		await Assert.That(after["mem-doctor"]).IsEqualTo(flatScore * 1.10).Within(1e-12);
		await Assert.That(after["mem-unnamed"]).IsEqualTo(flatScore);
	}

	[Test]
	public async ValueTask a_question_naming_no_stored_entity_ranks_exactly_as_it_would_without_entities() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var memories = await MemorySeeding.Seed(dataSources, Memory("mem-a"), Memory("mem-b"));

		await EntitySeeding.CreateSchema(dataSources);
		EntitySeeding.Insert(dataSources, new EntitySeed("ent-ada", "Ada Lovelace", "PERSON", Base));
		EntitySeeding.Insert(dataSources, new MentionSeed("ent-ada", "mem-a", "Ada Lovelace", Base));

		var entities = new KontextEntityIndex(new KontextEntityStore(dataSources));

		// Act
		var flat   = await Retrieve(Chain(memories, null));
		var nudged = await Retrieve(Chain(memories, entities));

		// Assert
		await Assert.That(Ids(nudged)).IsEquivalentTo(Ids(flat), TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(Scores(nudged)).IsEquivalentTo(Scores(flat));
	}

	[Test]
	public async ValueTask an_unprojected_read_model_is_skipped_instead_of_failing() {
		// The entity projector bootstraps its own schema when the node goes operational; a retrieval
		// that beats it must degrade to "no entities known", not take the request down with it.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var memories = await MemorySeeding.Seed(dataSources, Memory("mem-a"));

		var entities = new KontextEntityIndex(new KontextEntityStore(dataSources));

		var result = await Retrieve(Chain(memories, entities));

		await Assert.That(Ids(result)).IsEquivalentTo(["mem-a"]);
	}

	#region ->> Test Infrastructure <<-

	static ValueTask<IReadOnlyList<ScoredMemory>> Retrieve(IKontextRetriever retriever) =>
		retriever.RetrieveAsync(new() { Text = Question, AsOf = Base });

	static Dictionary<string, double> Scores(IEnumerable<ScoredMemory> ranked) =>
		ranked.ToDictionary(scored => scored.Memory.MemoryId, scored => scored.Score);

	static List<string> Ids(IEnumerable<ScoredMemory> ranked) =>
		ranked.Select(scored => scored.Memory.MemoryId).ToList();

	/// <summary>
	/// The keyword leg, fusion, modulation, then the entity nudge — no MMR, so the ranking the
	/// assertions read is the nudge's own output and nothing reorders behind it.
	/// </summary>
	static IKontextRetriever Chain(IMemoryIndex index, IEntityIndex? entities) =>
		KontextRetriever.From("entity-nudge",
			PlanStep.Default()
				.Then(new SearchStep(new KeywordSearch(index)))
				// Identity, not rank fusion: RRF scores by RANK, which would hand three identical
				// documents three different scores and hide what the nudge did.
				.Then(new FuseStep<NativeScale>(new IdentityFuser()))
				.Then(CognitiveModulator<NativeScale>.Create())
				.Then(EntityModulator<UnitScale>.Create(entities))
				.Then(new CutStep<UnitScale>()));

	/// <summary>Identical content and metadata: the pool reaches the nudge with nothing else to rank on.</summary>
	static MemoryRow Memory(string id) =>
		new(
			Id: id,
			Type: Contracts.MemoryType.Fact,
			Content: Content,
			Importance: Contracts.MemoryImportance.Normal,
			RetainedAt: Base) {
			LastAccessedAt = Base,
			Embedding      = EntitySeeding.Embedding(1f),
		};

	#endregion // Test Infrastructure
}
