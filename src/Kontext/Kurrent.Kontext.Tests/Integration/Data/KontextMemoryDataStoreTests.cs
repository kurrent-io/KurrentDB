// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Google.Protobuf;
using TUnit.Assertions.Enums;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.Data.LanceDB;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Testing;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextMemoryDataStore"/> against a REAL DuckDB + Lance engine.
/// The store is read-only, so each test seeds the memories table directly with SQL — exactly how
/// the projector will write it — through the same data sources the store reads from. No vector
/// store and no embedding model anywhere: embeddings are seeded as literal 4-dim vectors, and
/// searches pass the query embedding in.
/// </summary>
[Category("Integration")]
public class KontextMemoryDataStoreTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	static Contracts.Evidence SeedEvidence() => new() { Memory = new() { Id = "cited-1" } };

	// evidence is a VARCHAR[] column: one canonical-JSON citation per element.
	static List<string> SeedEvidenceBlobs() => [JsonFormatter.Default.Format(SeedEvidence())];

	[Test]
	public async ValueTask get_by_id_round_trips_every_field() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var stored = await store.GetAsync("m1");

		// Assert
		await Assert.That(stored).IsNotNull();
		await Assert.That(stored!.MemoryType).IsEqualTo((Contracts.MemoryType)1);
		await Assert.That(stored.Content).IsEqualTo("memory one");
		await Assert.That(stored.Importance).IsEqualTo((Contracts.MemoryImportance)3);
		await Assert.That(stored.Tags.Count).IsEqualTo(2);
		await Assert.That(stored.Tags[0].Scope).IsEqualTo("work");
		await Assert.That(stored.Tags[0].Value).IsEqualTo("alpha");
		await Assert.That(stored.Evidence.ToList()).IsEquivalentTo([SeedEvidence()]);
		await Assert.That(stored.ContentTime!.PerceivedStart.ToDateTimeOffset()).IsEqualTo(Base.AddHours(-24));
		await Assert.That(stored.ContentTime.PerceivedEnd.ToDateTimeOffset()).IsEqualTo(Base.AddHours(24));
		await Assert.That(stored.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base.AddHours(1));
		await Assert.That(stored.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(Base.AddHours(30));
		await Assert.That(stored.SupersededAt).IsNull();
		await Assert.That(stored.SupersededBy).IsEqualTo("");
	}

	[Test]
	public async ValueTask get_by_id_returns_null_for_missing_and_never_hides_lifecycle() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act + Assert
		await Assert.That(await store.GetAsync("no-such-memory")).IsNull();

		var superseded = await store.GetAsync("m4");
		await Assert.That(superseded!.SupersededAt).IsNotNull();
		await Assert.That(superseded.SupersededBy).IsEqualTo("m2");
	}

	[Test]
	public async ValueTask get_by_ids_returns_present_and_skips_missing() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var memories = await store.GetAsync(["m1", "m3", "no-such-memory"]).ToListAsync();

		// Assert
		await Assert.That(memories.Select(m => m.MemoryId).Order().ToList()).IsEquivalentTo(["m1", "m3"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_hides_retracted_keeps_superseded_and_sorts_by_retained_descending() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var memories = await store.ListAsync(
				[], [], Contracts.RecollectSort.RetainedAt,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		// Assert
		await Assert.That(memories.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m3", "m2", "m4", "m1", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_filters_by_any_of_types_and_all_tags() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act + Assert
		var byTypes = await store.ListAsync(
				[], [(Contracts.MemoryType)1, (Contracts.MemoryType)3],
				Contracts.RecollectSort.RetainedAt, Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		await Assert.That(byTypes.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m3", "m4", "m1"], CollectionOrdering.Matching);

		// ALL tags must be present: work:alpha AND team:blue.
		var byTags = await store.ListAsync(
				[Tag("work", "alpha"), Tag("team", "blue")], [],
				Contracts.RecollectSort.RetainedAt, Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		await Assert.That(byTags.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m4", "m1"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_sorts_by_importance_and_last_accessed_and_limits() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act + Assert
		var byImportance = await store.ListAsync(
				[], [], Contracts.RecollectSort.Importance,
				Contracts.SortDirection.Ascending, 10)
			.ToListAsync();

		await Assert.That(byImportance.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m2", "m4", "m1", "m3", "m5"], CollectionOrdering.Matching);

		// Last accessed descending, limited to the two freshest.
		var byAccess = await store.ListAsync(
				[], [], Contracts.RecollectSort.LastAccessedAt,
				Contracts.SortDirection.Descending, 2)
			.ToListAsync();

		await Assert.That(byAccess.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m5", "m1"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask lineage_returns_the_whole_family_from_any_member_in_chronological_order() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		SeedLineage(dataSources);

		var expected = new List<string> { "L1", "L2", "L3", "L4" };

		// Act
		var fromLeaf   = await store.GetLineageAsync("L1").ToListAsync();
		var fromMiddle = await store.GetLineageAsync("L3").ToListAsync();
		var fromHead   = await store.GetLineageAsync("L4").ToListAsync();

		// Assert
		await Assert.That(fromLeaf.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(fromMiddle.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(fromHead.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask lineage_of_an_unsuperseded_memory_is_itself_and_a_missing_id_streams_nothing() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act + Assert
		var alone = await store.GetLineageAsync("m1").ToListAsync();

		await Assert.That(alone.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m1"], CollectionOrdering.Matching);

		// A missing id streams nothing — the same silence as GetAsync.
		await Assert.That(await store.GetLineageAsync("no-such-memory").ToListAsync()).IsEmpty();
	}

	[Test]
	public async ValueTask lineage_keeps_the_up_chain_when_the_successor_has_a_one_sided_edge() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var family = await store.GetLineageAsync("m4").ToListAsync();

		// Assert
		await Assert.That(family.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m4", "m2"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_by_importance_breaks_ties_by_last_access_in_the_same_direction() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		SeedTieRows(dataSources);

		// Act
		var best = await store.ListAsync(
				[], [], Contracts.RecollectSort.Importance,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		var evict = await store.ListAsync(
				[], [], Contracts.RecollectSort.Importance,
				Contracts.SortDirection.Ascending, 10)
			.ToListAsync();

		// Assert
		await Assert.That(best.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m5", "m3", "m6", "m1", "m7", "m4", "m2"], CollectionOrdering.Matching);
		await Assert.That(evict.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m2", "m4", "m7", "m1", "m6", "m3", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_settles_exact_ties_deterministically_by_memory_id() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		SeedTieRows(dataSources);

		// Act
		var first = await store.ListAsync(
				[], [], Contracts.RecollectSort.RetainedAt,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		var second = await store.ListAsync(
				[], [], Contracts.RecollectSort.RetainedAt,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		// Assert
		var expected = new List<string> { "m6", "m7", "m3", "m2", "m4", "m1", "m5" };

		await Assert.That(first.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(second.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask search_ranks_by_vector_similarity_when_alpha_is_pure_vector() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var hits = await store.SearchAsync(
				"anything", MemorySeeding.Vector(1f), [],
				new() { Alpha = 1, Limit = 2 })
			.ToListAsync();

		// Assert
		await Assert.That(hits.Select(h => h.Memory.MemoryId).ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
		await Assert.That(hits[0].HybridScore!.Value).IsGreaterThanOrEqualTo(hits[1].HybridScore!.Value);
		await Assert.That(hits[0].VectorDistance!.Value).IsEqualTo(0);
		await Assert.That(hits[1].VectorDistance!.Value).IsGreaterThan(0);
	}

	[Test]
	public async ValueTask search_ranks_by_keywords_when_alpha_is_pure_text() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var hits = await store.SearchAsync(
				"projector checkpoint format", MemorySeeding.Vector(1f), [],
				new() { Alpha = 0, Limit = 1 })
			.ToListAsync();

		// Assert
		await Assert.That(hits.Count).IsEqualTo(1);
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("m2");
		await Assert.That(hits[0].KeywordScore!.Value).IsGreaterThan(0);
	}

	[Test]
	public async ValueTask search_never_surfaces_retracted_or_superseded_memories() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var zebra  = await store.SearchAsync("zebra", MemorySeeding.Vector(0f, 0f, 1f), []).ToListAsync();
		var quokka = await store.SearchAsync("quokka", MemorySeeding.Vector(0f, 0f, 0f, 1f), []).ToListAsync();

		// Assert
		await Assert.That(quokka.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m4");
	}

	[Test]
	public async ValueTask search_with_tags_prefilters_to_tagged_rows_only() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act — containment pushes down as a true prefilter: only tagged rows compete for k.
		var hits = await store.SearchAsync(
				"anything", MemorySeeding.Vector(0f, 1f), [Tag("team", "blue")],
				new() { K = 10, Limit = 10 })
			.ToListAsync();

		// Assert
		await Assert.That(hits.Select(h => h.Memory.MemoryId).Order().ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask vector_search_ranks_nearest_first_with_exact_match_at_distance_zero() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var hits = await store.SearchAsync(MemorySeeding.Vector(1f), [], new() { Limit = 2 }).ToListAsync();

		// Assert
		await Assert.That(hits.Select(h => h.Memory.MemoryId).ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
		await Assert.That(hits[0].VectorDistance!.Value).IsEqualTo(0);
		await Assert.That(hits[1].VectorDistance!.Value).IsGreaterThan(0);
		await Assert.That(hits[0].HybridScore).IsNull();
		await Assert.That(hits[0].KeywordScore).IsNull();
	}

	[Test]
	public async ValueTask vector_search_never_surfaces_retracted_or_superseded_memories() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var zebra  = await store.SearchAsync(MemorySeeding.Vector(0f, 0f, 1f), []).ToListAsync();
		var quokka = await store.SearchAsync(MemorySeeding.Vector(0f, 0f, 0f, 1f), []).ToListAsync();

		// Assert
		await Assert.That(quokka.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m4");
	}

	[Test]
	public async ValueTask vector_search_with_tags_prefilters_to_tagged_rows_only() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act — containment pushes down as a true prefilter: only tagged rows compete for k.
		var hits = await store.SearchAsync(MemorySeeding.Vector(0f, 1f), [Tag("team", "blue")], new() { K = 10, Limit = 10 }).ToListAsync();

		// Assert
		await Assert.That(hits.Select(h => h.Memory.MemoryId).Order().ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask fulltext_search_ranks_by_keywords_with_a_real_bm25_score() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var hits = await store.SearchAsync("projector checkpoint format", [], new FullTextSearchOptions { Limit = 1 }).ToListAsync();

		// Assert
		await Assert.That(hits.Count).IsEqualTo(1);
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("m2");
		await Assert.That(hits[0].KeywordScore!.Value).IsGreaterThan(0);
		await Assert.That(hits[0].VectorDistance).IsNull();
		await Assert.That(hits[0].HybridScore).IsNull();
	}

	[Test]
	public async ValueTask fulltext_search_never_surfaces_retracted_or_superseded_memories() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		// Act
		var zebra  = await store.SearchAsync("zebra", []).ToListAsync();
		var quokka = await store.SearchAsync("quokka", []).ToListAsync();

		// Assert
		await Assert.That(quokka.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m4");
	}

	[Test]
	public async ValueTask search_finds_the_exact_match_through_a_trained_ivf_hnsw_pq_index() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources);

		await SeedFillersAndCreateVectorIndex(dataSources);

		// The index must really exist — otherwise this test silently re-tests the brute-force path.
		var indexes = await dataSources.ExecuteAsync(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = $"SHOW INDEXES ON '{DatasetPath(dir.Path)}'";

			var       names  = new List<string>();
			using var reader = command.ExecuteReader();

			while (reader.Read())
				names.Add(reader.GetString(0));

			return names;
		});

		await Assert.That(indexes).Contains("embedding_ivx");

		// Act
		var hits = await store.SearchAsync(
				"anything", MemorySeeding.Vector(1f), [],
				new() { Alpha = 1, Limit = 1 })
			.ToListAsync();

		// Assert
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("m1");

		// The optional knobs must be accepted by the engine: nprobs bounds the IVF probe count,
		// use_index = false forces the exact brute-force path — same winner either way.
		var exact = await store.SearchAsync(
				"anything", MemorySeeding.Vector(1f), [],
				new() { Alpha = 1, Limit = 1, Nprobs = 1, UseIndex = false })
			.ToListAsync();

		await Assert.That(exact[0].Memory.MemoryId).IsEqualTo("m1");

		// The pure vector overload answers through the same trained index: exact-match top-1.
		var vector = await store.SearchAsync(MemorySeeding.Vector(1f), [], new() { Limit = 1 }).ToListAsync();

		await Assert.That(vector[0].Memory.MemoryId).IsEqualTo("m1");

		// And its optional knobs must be accepted by the engine too — same winner on the exact path.
		var vectorExact = await store.SearchAsync(
				MemorySeeding.Vector(1f), [],
				new() { Limit = 1, Nprobs = 1, UseIndex = false })
			.ToListAsync();

		await Assert.That(vectorExact[0].Memory.MemoryId).IsEqualTo("m1");
	}

	[Test]
	public async ValueTask schema_create_is_idempotent_and_creates_the_eager_indexes() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		// Act
		await MemorySeeding.CreateSchema(dataSources);
		await MemorySeeding.CreateSchema(dataSources);

		// Assert
		var indexes = AllIndexNames(dataSources);

		await Assert.That(indexes).Contains("content_fts");
		await Assert.That(indexes).Contains("memory_id_idx");
		await Assert.That(indexes).Contains("superseded_by_idx");
		await Assert.That(indexes).Contains("tags_idx");
		await Assert.That(indexes).DoesNotContain("embedding_ivx");
	}

	[Test]
	public async ValueTask schema_vector_index_waits_for_the_training_floor() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await Seed(dataSources);

		// Act
		var ready = dataSources.Execute(c => c.EnsureVectorIndex("ldb.main.memories", "embedding", new LanceIvfPqIndexOptions { NumPartitions = 1, NumSubVectors = KontextIndexConstants.VectorsDimension / 8 }));

		// Assert
		await Assert.That(ready).IsFalse();
		await Assert.That(dataSources.Execute(c => c.GetTableInfo("ldb.main.memories")!.FindIndex(LanceIndexNames.Vector("embedding")))?.Name).IsNull();
	}

	#region ->> Test Infrastructure <<-

	static Contracts.Tag Tag(string scope, string value) => new() { Scope = scope, Value = value };

	/// <summary>Creates the schema through <see cref="KontextMigrations"/> and seeds the five fixed rows, then hands back a store over the same data sources.</summary>
	static async ValueTask<KontextMemoryDataStore> Seed(KontextDataSource dataSource) {
		// The schema component owns CREATE TABLE and every eager index (including the FTS
		// INVERTED index the keyword tests need) — seeding only inserts rows.
		await MemorySeeding.CreateSchema(dataSource);

		// One multi-row INSERT: five fixed tuples, nineteen parameters each, bound row by row
		// in AddRow's column order. Kept apart from the schema DDL because parameters don't
		// prepare across a multi-statement batch — the one justified exception to
		// single-command batching.
		const string insertRowsSql =
			"""
			INSERT INTO ldb.main.memories (
			  memory_id,
			  memory_type,
			  content,
			  importance,
			  tags,
			  reasoning,
			  evidence,
			  supersedes,
			  content_time_start,
			  content_time_end,
			  retained_at,
			  last_accessed_at,
			  is_superseded,
			  superseded_at,
			  superseded_by,
			  embedding)
			VALUES
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
			""";

		dataSource.Execute(connection => {
			using (var insert = connection.CreateCommand()) {
				insert.CommandText = insertRowsSql;

				// Each row sits on its own vector axis, so vector-search tests are deterministic;
				// contents carry distinct words for the keyword-search tests.

				// m5: earliest retained, freshest access, highest importance; near m1's axis.
				AddRow(insert, "m5", 2, "quick brown dog runs", 5, ["team:blue"], "", [], [],
					null, null, Base, Base.AddHours(40), false, null, "", MemorySeeding.Vector(0.9f, 0.1f));

				// m1: the full-field row — evidence with a citation, content-time window, two tags.
				AddRow(insert, "m1", 1, "memory one", 3, ["work:alpha", "team:blue"], "", SeedEvidenceBlobs(), [],
					Base.AddHours(-24), Base.AddHours(24), Base.AddHours(1), Base.AddHours(30), false, null, "", MemorySeeding.Vector(1f));

				// m4: superseded by m2 — visible in listings, marked in reads, hidden from search.
				AddRow(insert, "m4", 3, "legacy quokka wisdom", 2, ["work:alpha", "team:blue"], "", [], [],
					null, null, Base.AddHours(2), Base.AddHours(10), true, Base.AddHours(3), "m2", MemorySeeding.Vector(0f, 0f, 0f, 1f));

				// m2: latest retained, lowest importance.
				AddRow(insert, "m2", 2, "projector checkpoint format switched", 1, ["work:alpha"], "", [], [],
					null, null, Base.AddHours(3), Base.AddHours(20), false, null, "", MemorySeeding.Vector(0f, 1f));

				// m3: retracted — hidden from listings and search, still readable by id.
				AddRow(insert, "m3", 1, "secret zebra fact", 4, [], "", [], [],
					null, null, Base.AddHours(4), Base.AddHours(4), false, null, "", MemorySeeding.Vector(0f, 0f, 1f));

				insert.ExecuteNonQuery();
			}
		});

		return new(dataSource);
	}

	/// <summary>Crosses the vector-index training floor with 300 filler rows, then creates the IVF_HNSW_PQ index through <see cref="KontextSchema"/>.</summary>
	static async ValueTask SeedFillersAndCreateVectorIndex(KontextDataSource dataSource) {
		// Fillers are generated ENGINE-SIDE: one statement, no parameters, deterministic. Their
		// vectors spread over the (z, w) circle, far from every axis the search tests query.
		var fillersSql =
			$"""
			INSERT INTO ldb.main.memories (
			  memory_id,
			  memory_type,
			  content,
			  importance,
			  tags,
			  reasoning,
			  evidence,
			  supersedes,
			  content_time_start,
			  content_time_end,
			  retained_at,
			  last_accessed_at,
			  is_superseded,
			  superseded_at,
			  superseded_by,
			  embedding)
			SELECT 'filler-' || i,
			       1,
			       'filler content ' || i,
			       0,
			       CAST([] AS VARCHAR[]),
			       '',
			       CAST([] AS VARCHAR[]),
			       CAST([] AS VARCHAR[]),
			       NULL,
			       NULL,
			       epoch_ms(TIMESTAMPTZ '2026-06-01 00:00:00+00'),
			       epoch_ms(TIMESTAMPTZ '2026-06-01 00:00:00+00'),
			       false,
			       NULL,
			       '',
			       CAST(list_concat([0.1, 0.1, cos(i), sin(i)], list_transform(range({KontextIndexConstants.VectorsDimension - 4}), lambda x: 0.0)) AS FLOAT[{KontextIndexConstants.VectorsDimension}])
			FROM range(300) AS t(i)
			""";

		dataSource.Execute(connection => {
			using var fillers = connection.CreateCommand();
			fillers.CommandText = fillersSql;
			fillers.ExecuteNonQuery();
		});

		// The schema component owns the vector index; the fillers above just crossed the
		// training floor, so the first call must create it. The second call exercises the
		// other half of the lifecycle against the real engine: index exists => append-optimize.
		await Assert.That(dataSource.Execute(c => c.EnsureVectorIndex("ldb.main.memories", "embedding", new LanceIvfPqIndexOptions { NumPartitions = 1, NumSubVectors = KontextIndexConstants.VectorsDimension / 8 }))).IsTrue();
		await Assert.That(dataSource.Execute(c => c.EnsureVectorIndex("ldb.main.memories", "embedding", new LanceIvfPqIndexOptions { NumPartitions = 1, NumSubVectors = KontextIndexConstants.VectorsDimension / 8 }))).IsTrue();
	}

	/// <summary>One complete supersession family: L4 (living head) replaced L3, which had consolidated L1 and L2.</summary>
	static void SeedLineage(KontextDataSource dataSource) {
		// Literal, engine-side values — no parameters, fully deterministic:
		// - the tree: L4 -> L3 -> {L1, L2}; only L4 is still living
		// - edges are SYMMETRIC (each superseded_by has its matching supersedes entry),
		//   the way the projector writes them
		// - retained_at ascends L1 < L2 < L3 < L4, so chronological order is the tree bottom-up
		// - embeddings sit away from every axis the search tests query
		var sql =
			$"""
			INSERT INTO ldb.main.memories (
			  memory_id,
			  memory_type,
			  content,
			  importance,
			  tags,
			  reasoning,
			  evidence,
			  supersedes,
			  content_time_start,
			  content_time_end,
			  retained_at,
			  last_accessed_at,
			  is_superseded,
			  superseded_at,
			  superseded_by,
			  embedding)
			VALUES
			  ('L1', 1, 'lineage first belief', 1, CAST([] AS VARCHAR[]), '', CAST([] AS VARCHAR[]), CAST([] AS VARCHAR[]),
			   NULL, NULL, epoch_ms(TIMESTAMPTZ '2026-07-01 15:00:00+00'), epoch_ms(TIMESTAMPTZ '2026-07-01 15:00:00+00'),
			   true, epoch_ms(TIMESTAMPTZ '2026-07-01 17:00:00+00'), 'L3', CAST(list_concat([0.0, 0.0, 0.6, 0.8], list_transform(range({KontextIndexConstants.VectorsDimension - 4}), lambda x: 0.0)) AS FLOAT[{KontextIndexConstants.VectorsDimension}])),
			  ('L2', 1, 'lineage second belief', 1, CAST([] AS VARCHAR[]), '', CAST([] AS VARCHAR[]), CAST([] AS VARCHAR[]),
			   NULL, NULL, epoch_ms(TIMESTAMPTZ '2026-07-01 16:00:00+00'), epoch_ms(TIMESTAMPTZ '2026-07-01 16:00:00+00'),
			   true, epoch_ms(TIMESTAMPTZ '2026-07-01 17:00:00+00'), 'L3', CAST(list_concat([0.0, 0.0, 0.8, 0.6], list_transform(range({KontextIndexConstants.VectorsDimension - 4}), lambda x: 0.0)) AS FLOAT[{KontextIndexConstants.VectorsDimension}])),
			  ('L3', 1, 'lineage consolidated belief', 2, CAST([] AS VARCHAR[]), '', CAST([] AS VARCHAR[]), CAST(['L1', 'L2'] AS VARCHAR[]),
			   NULL, NULL, epoch_ms(TIMESTAMPTZ '2026-07-01 17:00:00+00'), epoch_ms(TIMESTAMPTZ '2026-07-01 17:00:00+00'),
			   true, epoch_ms(TIMESTAMPTZ '2026-07-01 18:00:00+00'), 'L4', CAST(list_concat([0.0, 0.0, 0.7, 0.7], list_transform(range({KontextIndexConstants.VectorsDimension - 4}), lambda x: 0.0)) AS FLOAT[{KontextIndexConstants.VectorsDimension}])),
			  ('L4', 1, 'lineage current belief', 3, CAST([] AS VARCHAR[]), '', CAST([] AS VARCHAR[]), CAST(['L3'] AS VARCHAR[]),
			   NULL, NULL, epoch_ms(TIMESTAMPTZ '2026-07-01 18:00:00+00'), epoch_ms(TIMESTAMPTZ '2026-07-01 18:00:00+00'),
			   false, NULL, '', CAST(list_concat([0.0, 0.0, 0.5, 0.9], list_transform(range({KontextIndexConstants.VectorsDimension - 4}), lambda x: 0.0)) AS FLOAT[{KontextIndexConstants.VectorsDimension}]))
			""";

		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.ExecuteNonQuery();
		});
	}

	/// <summary>Two extra rows that TIE on purpose — the raw material for the ordering-guarantee tests.</summary>
	static void SeedTieRows(KontextDataSource dataSource) {
		// Literal, engine-side values — no parameters, fully deterministic:
		// - importance 3 matches m1, so {m7, m1, m6} form one importance tie group
		// - last access ranks inside that group: m6 (Base+50h) > m1 (Base+30h) > m7 (Base+10h)
		// - m6 and m7 share retained_at (Base+6h) EXACTLY, so only memory_id can order them
		// - embeddings sit away from every axis the search tests query
		var sql =
			$"""
			INSERT INTO ldb.main.memories (
			  memory_id,
			  memory_type,
			  content,
			  importance,
			  tags,
			  reasoning,
			  evidence,
			  supersedes,
			  content_time_start,
			  content_time_end,
			  retained_at,
			  last_accessed_at,
			  is_superseded,
			  superseded_at,
			  superseded_by,
			  embedding)
			VALUES
			  ('m6', 1, 'tie row six', 3, CAST([] AS VARCHAR[]), '', CAST([] AS VARCHAR[]), CAST([] AS VARCHAR[]),
			   NULL, NULL, epoch_ms(TIMESTAMPTZ '2026-07-01 16:00:00+00'), epoch_ms(TIMESTAMPTZ '2026-07-03 12:00:00+00'),
			   false, NULL, '', CAST(list_concat([0.5, 0.5, 0.0, 0.0], list_transform(range({KontextIndexConstants.VectorsDimension - 4}), lambda x: 0.0)) AS FLOAT[{KontextIndexConstants.VectorsDimension}])),
			  ('m7', 1, 'tie row seven', 3, CAST([] AS VARCHAR[]), '', CAST([] AS VARCHAR[]), CAST([] AS VARCHAR[]),
			   NULL, NULL, epoch_ms(TIMESTAMPTZ '2026-07-01 16:00:00+00'), epoch_ms(TIMESTAMPTZ '2026-07-01 20:00:00+00'),
			   false, NULL, '', CAST(list_concat([0.4, 0.6, 0.0, 0.0], list_transform(range({KontextIndexConstants.VectorsDimension - 4}), lambda x: 0.0)) AS FLOAT[{KontextIndexConstants.VectorsDimension}]))
			""";

		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.ExecuteNonQuery();
		});
	}

	static string DatasetPath(string dir) => System.IO.Path.Combine(System.IO.Path.GetFullPath(dir), "memories.lance");

	// Binds one VALUES tuple, in InsertRowsSql's column order; null binds as NULL.
	static void AddRow(
		DuckDBCommand command,
		string memoryId, int memoryType, string content, int importance,
		List<string> tags, string reasoning, List<string> evidence, List<string> supersedes,
		DateTimeOffset? contentTimeStart, DateTimeOffset? contentTimeEnd,
		DateTimeOffset retainedAt, DateTimeOffset lastAccessedAt,
		bool isSuperseded, DateTimeOffset? supersededAt, string supersededBy,
		float[] embedding
	) {
		// Timestamps bind as Unix epoch milliseconds — the schema's BIGINT columns.
		object?[] values = [
			memoryId, memoryType, content, importance, tags, reasoning, evidence,
			supersedes, contentTimeStart?.ToUnixTimeMilliseconds(), contentTimeEnd?.ToUnixTimeMilliseconds(),
			retainedAt.ToUnixTimeMilliseconds(), lastAccessedAt.ToUnixTimeMilliseconds(),
			isSuperseded, supersededAt?.ToUnixTimeMilliseconds(),
			supersededBy, embedding,
		];

		foreach (var value in values)
			command.Parameters.Add(new DuckDBParameter(value ?? DBNull.Value));
	}

	static KontextDataSource NewDataSources(string dir) => MemorySeeding.NewDataSources(dir);


	/// <summary>Every index name on the memories dataset — the maintenance surface lists only vector indexes.</summary>
	static List<string> AllIndexNames(KontextDataSource dataSource) =>
		dataSource.Execute(static connection => {
			using var result = connection.ExecuteAdHocQuery("SHOW INDEXES ON ldb.main.memories"u8);

			var names = new List<string>();

			while (result.TryFetch(out var chunk)) {
				while (chunk.TryRead(out var row))
					names.Add(row.ReadString());

				chunk.Dispose();
			}

			return names;
		});


	#endregion // Test Infrastructure
}
