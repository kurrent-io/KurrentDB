// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Resolution;

namespace Kurrent.Kontext.Tests.Integration.Modules.Entities;

/// <summary>
/// The resolver chain against a REAL DuckDB + Lance store: equality, typo distance, and cosine
/// each finding what only they can find, and the type wall standing throughout. Embeddings are
/// literal 4-dim vectors — the semantic leg's model is the caller's concern by design.
/// </summary>
[Category("Integration")]
[Category("Entities")]
public class EntityResolverTests {
	static readonly DateTimeOffset Base = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask exact_resolver_matches_names_and_aliases_within_the_type() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources, new EntitySeed("ent-1", "Ada Lovelace", "PERSON", Base) {
			Aliases = ["ada lovelace", "countess of lovelace"]
		});

		var resolver = new ExactEntityResolver(new(dataSources));

		var byName = await resolver.ResolveAsync(new EntityProbe("  ADA   Lovelace ", "PERSON"));
		await Assert.That(byName.IsMatch).IsTrue();
		await Assert.That(byName.Match!.EntityId).IsEqualTo("ent-1");
		await Assert.That(byName.Method).IsEqualTo(ResolutionMethod.Exact);
		await Assert.That(byName.Score).IsEqualTo(1.0);

		var byAlias = await resolver.ResolveAsync(new EntityProbe("Countess of Lovelace", "PERSON"));
		await Assert.That(byAlias.Match!.EntityId).IsEqualTo("ent-1");

		var wrongType = await resolver.ResolveAsync(new EntityProbe("Ada Lovelace", "LOCATION"));
		await Assert.That(wrongType.IsMatch).IsFalse();
	}

	[Test]
	public async ValueTask fuzzy_resolver_catches_typos_and_word_order_but_not_strangers() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "John Smith", "PERSON", Base),
			new EntitySeed("ent-2", "Jane Doe", "PERSON", Base));

		var resolver = new FuzzyEntityResolver(new(dataSources));

		var typo = await resolver.ResolveAsync(new EntityProbe("Jon Smith", "PERSON"));
		await Assert.That(typo.Match!.EntityId).IsEqualTo("ent-1");
		await Assert.That(typo.Method).IsEqualTo(ResolutionMethod.Fuzzy);
		await Assert.That(typo.Score).IsGreaterThanOrEqualTo(0.85);

		var reordered = await resolver.ResolveAsync(new EntityProbe("Smith,  John", "PERSON"));
		await Assert.That(reordered.Match!.EntityId).IsEqualTo("ent-1");

		var stranger = await resolver.ResolveAsync(new EntityProbe("Grace Hopper", "PERSON"));
		await Assert.That(stranger.IsMatch).IsFalse();
	}

	[Test]
	public async ValueTask fuzzy_resolver_scores_aliases_too() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "International Business Machines", "ORGANIZATION", Base) { Aliases = ["international business machines", "ibm corp"] });

		var resolver = new FuzzyEntityResolver(new(dataSources));

		var viaAlias = await resolver.ResolveAsync(new EntityProbe("IBM Corp.", "ORGANIZATION"));
		await Assert.That(viaAlias.Match!.EntityId).IsEqualTo("ent-1");
	}

	[Test]
	public async ValueTask semantic_resolver_needs_an_embedding_and_honors_threshold_and_type() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "Kurrent", "ORGANIZATION", Base) { Embedding = EntitySeeding.Embedding(1f, 0f, 0f, 0f) },
			new EntitySeed("ent-2", "Cheesecake", "ORGANIZATION", Base) { Embedding = EntitySeeding.Embedding(0f, 1f, 0f, 0f) },
			new EntitySeed("ent-3", "Kurrent HQ", "LOCATION", Base) { Embedding = EntitySeeding.Embedding(1f, 0f, 0f, 0f) });

		var resolver = new SemanticEntityResolver(new(dataSources));

		var withoutEmbedding = await resolver.ResolveAsync(new EntityProbe("KurrentDB", "ORGANIZATION"));
		await Assert.That(withoutEmbedding.IsMatch).IsFalse();

		var near = await resolver.ResolveAsync(new EntityProbe("KurrentDB", "ORGANIZATION", EntitySeeding.Embedding(0.95f, 0.05f)));
		await Assert.That(near.Match!.EntityId).IsEqualTo("ent-1");
		await Assert.That(near.Method).IsEqualTo(ResolutionMethod.Semantic);
		await Assert.That(near.Score).IsGreaterThanOrEqualTo(0.8);

		var far = await resolver.ResolveAsync(new EntityProbe("Unrelated", "ORGANIZATION", EntitySeeding.Embedding(0.5f, 0.5f, 0.5f, 0.5f)));
		await Assert.That(far.IsMatch).IsFalse();
	}

	[Test]
	public async ValueTask composite_chain_prefers_exact_then_fuzzy_then_semantic() {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-1", "Kurrent", "ORGANIZATION", Base) { Embedding = EntitySeeding.Embedding(1f, 0f, 0f, 0f) },
			new EntitySeed("ent-2", "Neo4j", "ORGANIZATION", Base) { Embedding = EntitySeeding.Embedding(0f, 1f, 0f, 0f) });

		var store    = new KontextEntityStore(dataSources);
		var resolver = CompositeEntityResolver.Over(store);

		var exact = await resolver.ResolveAsync(new EntityProbe("kurrent", "ORGANIZATION"));
		await Assert.That(exact.Method).IsEqualTo(ResolutionMethod.Exact);

		var fuzzy = await resolver.ResolveAsync(new EntityProbe("Kurent", "ORGANIZATION"));
		await Assert.That(fuzzy.Method).IsEqualTo(ResolutionMethod.Fuzzy);
		await Assert.That(fuzzy.Match!.EntityId).IsEqualTo("ent-1");

		// A name no string metric can bridge, carried by its embedding alone.
		var semantic = await resolver.ResolveAsync(new EntityProbe("The event-native database company", "ORGANIZATION", EntitySeeding.Embedding(0.98f, 0.02f)));
		await Assert.That(semantic.Method).IsEqualTo(ResolutionMethod.Semantic);
		await Assert.That(semantic.Match!.EntityId).IsEqualTo("ent-1");

		var nothing = await resolver.ResolveAsync(new EntityProbe("Zebra", "ORGANIZATION"));
		await Assert.That(nothing.IsMatch).IsFalse();
		await Assert.That(nothing.Method).IsEqualTo(ResolutionMethod.None);
	}
}
