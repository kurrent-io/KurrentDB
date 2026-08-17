// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;

namespace Kurrent.Kontext.Tests.Integration.Modules.Entities.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextSchemaTask"/> and <see cref="KontextEntityStore"/>
/// against a REAL DuckDB + Lance engine. The store is read-only, so each test seeds the entity
/// tables directly with SQL — exactly how the projector will write them. Probe geometry is
/// hand-written 4-dim, zero-padded to the schema's dimension by <see cref="EntitySeeding.Embedding"/>.
/// </summary>
[Category("Integration")]
[Category("Entities")]
public class KontextEntityStoreTests {
	static readonly DateTimeOffset Base = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask schema_creation_is_idempotent_and_the_store_reports_it() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var store = new KontextEntityStore(dataSources);

		await Assert.That(await store.ExistsAsync()).IsFalse();

		await EntitySeeding.CreateSchema(dataSources);
		await EntitySeeding.CreateSchema(dataSources);

		await Assert.That(await store.ExistsAsync()).IsTrue();
	}

	[Test]
	public async ValueTask get_returns_the_full_row_and_misses_return_null() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources, new EntitySeed("ent-1", "Ada Lovelace", "PERSON", Base) {
			Aliases      = ["ada lovelace", "ada"],
			MentionCount = 3,
			Confidence   = 0.9,
			LogPosition  = 42,
		});

		var store = new KontextEntityStore(dataSources);

		var entity = await store.GetAsync("ent-1");

		await Assert.That(entity).IsNotNull();
		await Assert.That(entity!.Name).IsEqualTo("Ada Lovelace");
		await Assert.That(entity.NormalizedName).IsEqualTo("ada lovelace");
		await Assert.That(entity.EntityType).IsEqualTo("PERSON");
		await Assert.That(entity.Aliases).IsEquivalentTo(["ada lovelace", "ada"]);
		await Assert.That(entity.MentionCount).IsEqualTo(3);
		await Assert.That(entity.LogPosition).IsEqualTo(42);

		await Assert.That(await store.GetAsync("ent-none")).IsNull();
	}

	[Test]
	public async ValueTask find_exact_matches_normalized_name_and_aliases_within_the_type() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "Ada Lovelace", "PERSON", Base) { Aliases = ["ada lovelace", "countess of lovelace"] },
			new EntitySeed("ent-2", "Lovelace", "LOCATION", Base));

		var store = new KontextEntityStore(dataSources);

		var byName = await store.FindExactAsync("ada lovelace", "PERSON");
		await Assert.That(byName!.EntityId).IsEqualTo("ent-1");

		var byAlias = await store.FindExactAsync("countess of lovelace", "PERSON");
		await Assert.That(byAlias!.EntityId).IsEqualTo("ent-1");

		// The same key under another type never crosses over.
		await Assert.That(await store.FindExactAsync("ada lovelace", "LOCATION")).IsNull();
	}

	[Test]
	public async ValueTask list_by_type_orders_by_mention_count_and_caps_at_the_limit() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "Kurrent", "ORGANIZATION", Base) { MentionCount = 5 },
			new EntitySeed("ent-2", "Neo4j", "ORGANIZATION", Base) { MentionCount = 9 },
			new EntitySeed("ent-3", "London", "LOCATION", Base) { MentionCount = 7 },
			new EntitySeed("ent-4", "Microsoft", "ORGANIZATION", Base) { MentionCount = 1 });

		var store = new KontextEntityStore(dataSources);

		var organizations = await store.ListByTypeAsync("ORGANIZATION", limit: 2);

		await Assert.That(organizations.Select(entity => entity.EntityId).ToList())
			.IsEquivalentTo(["ent-2", "ent-1"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask search_similar_ranks_by_cosine_within_the_type() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "Kurrent", "ORGANIZATION", Base) { Embedding = EntitySeeding.Embedding(1f, 0f, 0f, 0f) },
			new EntitySeed("ent-2", "KurrentDB", "ORGANIZATION", Base) { Embedding = EntitySeeding.Embedding(0.9f, 0.1f, 0f, 0f) },
			new EntitySeed("ent-3", "Cheesecake", "ORGANIZATION", Base) { Embedding = EntitySeeding.Embedding(0f, 0f, 1f, 0f) },
			new EntitySeed("ent-4", "Kurrent HQ", "LOCATION", Base) { Embedding = EntitySeeding.Embedding(1f, 0f, 0f, 0f) });

		var store = new KontextEntityStore(dataSources);

		var hits = await store.SearchSimilarAsync(EntitySeeding.Embedding(1f, 0f, 0f, 0f), "ORGANIZATION", k: 2);

		await Assert.That(hits.Select(hit => hit.Entity.EntityId).ToList())
			.IsEquivalentTo(["ent-1", "ent-2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(hits[0].CosineSimilarity).IsEqualTo(1.0).Within(0.0001);
		await Assert.That(hits[1].CosineSimilarity).IsGreaterThan(0.9);
	}

	[Test]
	public async ValueTask mentions_walk_both_directions() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-1", "mem-1", "Ada Lovelace", Base) { StartPos = 0, EndPos = 12, Extractor = "catalyst-wikiner" },
			new MentionSeed("ent-1", "mem-2", "Ada", Base.AddMinutes(1)),
			new MentionSeed("ent-2", "mem-1", "London", Base) { StartPos = 20, EndPos = 26 });

		var store = new KontextEntityStore(dataSources);

		var ofMemory = await store.ListMentionsOfMemoryAsync("mem-1");
		await Assert.That(ofMemory.Select(mention => mention.EntityId).ToList()).IsEquivalentTo(["ent-1", "ent-2"]);

		var first = ofMemory.Single(mention => mention.EntityId == "ent-1");
		await Assert.That(first.Surface).IsEqualTo("Ada Lovelace");
		await Assert.That(first.StartPos).IsEqualTo(0);
		await Assert.That(first.EndPos).IsEqualTo(12);
		await Assert.That(first.Extractor).IsEqualTo("catalyst-wikiner");

		var ofEntity = await store.ListMentionsOfEntityAsync("ent-1");
		await Assert.That(ofEntity.Select(mention => mention.MemoryId).ToList()).IsEquivalentTo(["mem-1", "mem-2"]);
	}

	[Test]
	public async ValueTask links_list_by_status_oldest_first() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new LinkSeed("ent-2", "ent-1", Base.AddMinutes(2)) { Confidence = 0.88, Method = "semantic" },
			new LinkSeed("ent-4", "ent-3", Base.AddMinutes(1)) { Confidence = 0.91, Method = "fuzzy" },
			new LinkSeed("ent-6", "ent-5", Base) { Status = "confirmed" });

		var store = new KontextEntityStore(dataSources);

		var pending = await store.ListLinksAsync("pending", limit: 10);

		await Assert.That(pending.Select(link => link.SourceEntityId).ToList())
			.IsEquivalentTo(["ent-4", "ent-2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(pending[0].Method).IsEqualTo("fuzzy");
		await Assert.That(pending[1].Confidence).IsEqualTo(0.88);
	}

	[Test]
	public async ValueTask count_reflects_the_seeded_population() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "Kurrent", "ORGANIZATION", Base),
			new EntitySeed("ent-2", "Ada Lovelace", "PERSON", Base));

		var store = new KontextEntityStore(dataSources);

		await Assert.That(await store.CountAsync()).IsEqualTo(2);
	}
}
