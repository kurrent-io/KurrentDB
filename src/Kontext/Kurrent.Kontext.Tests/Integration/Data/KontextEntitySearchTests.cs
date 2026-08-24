// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Memory.Data;
using DuckDB.NET.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Retrieval;
using TUnit.Assertions.Enums;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for the entity leg of <see cref="KontextMemoryDataStore"/> — the
/// <see cref="IEntityIndex"/> surface — against a REAL DuckDB + Lance engine. Each test seeds
/// memories through <see cref="MemorySeeding"/> and the catalog tables directly with SQL, exactly
/// how the projectors write them. The leg never reads embeddings, so aliases carry zero vectors.
/// </summary>
[Category("Integration")]
public class KontextEntitySearchTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask matches_aliases_on_whole_words_ignoring_case_and_punctuation() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		IEntityIndex index = await Seed(dataSources);

		// Act — "Acme-Corp" collapses to the stored alias "Acme Corp"; m3 and m4 (both
		// superseded) mention the same entity and must stay invisible.
		var hits = await index.SearchAsync("Who runs Acme-Corp?", []).ToListAsync();

		// Assert — both active memories name acme and nothing else (df = N = 2, idf = ln 2), so
		// they tie exactly — the leg has no opinion between them — and memory id breaks the tie.
		await Assert.That(hits.Select(hit => hit.Memory.MemoryId).ToList())
			.IsEquivalentTo(["m1", "m2"], CollectionOrdering.Matching);

		await Assert.That(hits[0].EntityScore).IsEqualTo(0.6931).Within(0.001);
		await Assert.That(hits[1].EntityScore).IsEqualTo(0.6931).Within(0.001);

		// Act + Assert — the catalog holds "Art", and "art" occurs inside "started", but a needle
		// only matches on word boundaries. A query naming nothing at all finds nothing either.
		await Assert.That(await index.SearchAsync("we started something new", []).ToListAsync()).IsEmpty();
		await Assert.That(await index.SearchAsync("completely unrelated words", []).ToListAsync()).IsEmpty();
	}

	[Test]
	public async ValueTask scores_sum_idf_once_per_distinct_entity() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		IEntityIndex index = await Seed(dataSources);

		// Act — the query names two entities: m1 mentions acme twice (counted once) plus zenith;
		// m2 mentions acme alone. With N = 2 active memories, acme's idf is ln 2 (df 2) and
		// zenith's ln 3 (df 1): m1 = ln 2 + ln 3 = 1.7918, m2 = ln 2 = 0.6931.
		var hits = await index.SearchAsync("Acme and Zenith", []).ToListAsync();

		// Assert
		await Assert.That(hits.Select(hit => hit.Memory.MemoryId).ToList())
			.IsEquivalentTo(["m1", "m2"], CollectionOrdering.Matching);

		await Assert.That(hits[0].EntityScore).IsEqualTo(1.7918).Within(0.001);
		await Assert.That(hits[1].EntityScore).IsEqualTo(0.6931).Within(0.001);

		// Assert — the limit caps the ranked list from the top.
		var capped = await index.SearchAsync("Acme and Zenith", [], new EntitySearchOptions { Limit = 1 }).ToListAsync();

		await Assert.That(capped.Select(hit => hit.Memory.MemoryId).ToList())
			.IsEquivalentTo(["m1"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask every_requested_tag_must_be_present() {
		// Arrange — m1 carries work:alpha, m2 does not; both mention acme.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		IEntityIndex index = await Seed(dataSources);

		// Act + Assert
		var tagged = await index.SearchAsync(
			"Acme", [new MemoryContracts.Tag { Scope = "work", Value = "alpha" }]).ToListAsync();

		await Assert.That(tagged.Select(hit => hit.Memory.MemoryId).ToList())
			.IsEquivalentTo(["m1"], CollectionOrdering.Matching);

		var impossible = await index.SearchAsync(
			"Acme", [new MemoryContracts.Tag { Value = "no-such-tag" }]).ToListAsync();

		await Assert.That(impossible).IsEmpty();
	}

	#region ->> Test Infrastructure <<-

	/// <summary>
	/// The corpus every test reads:
	/// - m1 (live, work:alpha + research) mentions acme at 0.9 and 0.5, zenith at 0.8
	/// - m2 (live, research) mentions acme at 1.0 and art at 1.0
	/// - m3 and m4 (superseded) mention acme at 1.0 — the lifecycle guard
	/// </summary>
	static async ValueTask<KontextMemoryDataStore> Seed(KontextDataSource dataSource) {
		// The entity leg never reads embeddings; the schema-dimension zero vector just keeps the
		// rows well-formed.
		var embedding = new float[KontextIndexConstants.VectorsDimension];

		var store = await MemorySeeding.Seed(dataSource,
			new MemoryRow("m1", MemoryContracts.MemoryType.Fact, "the quarterly report", MemoryContracts.MemoryImportance.Normal, Base) {
				Tags = ["work:alpha", "research"], Embedding = embedding
			},
			new MemoryRow("m2", MemoryContracts.MemoryType.Fact, "the hiring plan", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1)) {
				Tags = ["research"], Embedding = embedding
			},
			new MemoryRow("m3", MemoryContracts.MemoryType.Fact, "an old note", MemoryContracts.MemoryImportance.Normal, Base.AddHours(2)) {
				IsSuperseded = true, SupersededAt = Base.AddHours(3), SupersededBy = "m1", Embedding = embedding
			},
			new MemoryRow("m4", MemoryContracts.MemoryType.Fact, "an old summary", MemoryContracts.MemoryImportance.Normal, Base.AddHours(2)) {
				IsSuperseded = true, SupersededAt = Base.AddHours(3), SupersededBy = "m2", Embedding = embedding
			});

		InsertEntities(dataSource,
			("e-acme", "organization", "Acme Corp"),
			("e-acme", "organization", "Acme"),
			("e-zenith", "organization", "Zenith"),
			("e-art", "project", "Art"));

		InsertMentions(dataSource,
			("m1", 0, "Acme", "e-acme", 0.9f),
			("m1", 1, "acme", "e-acme", 0.5f),
			("m1", 2, "Zenith", "e-zenith", 0.8f),
			("m2", 0, "Acme Corp", "e-acme", 1.0f),
			("m2", 1, "Art", "e-art", 1.0f),
			("m3", 0, "Acme", "e-acme", 1.0f),
			("m4", 0, "Acme", "e-acme", 1.0f));

		return store;
	}

	static void InsertEntities(KontextDataSource dataSource, params (string Id, string Type, string Alias)[] rows) {
		var sql =
			"INSERT INTO ldb.main.entities (entity_id, entity_type, alias, first_seen_at, embedding)\nVALUES "
			+ string.Join(", ", Enumerable.Repeat("(?, ?, ?, 0, ?)", rows.Length));

		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = sql;

			foreach (var row in rows) {
				command.Parameters.Add(new DuckDBParameter(row.Id));
				command.Parameters.Add(new DuckDBParameter(row.Type));
				command.Parameters.Add(new DuckDBParameter(row.Alias));
				command.Parameters.Add(new DuckDBParameter(new float[KontextIndexConstants.VectorsDimension]));
			}

			command.ExecuteNonQuery();
		});
	}

	static void InsertMentions(
		KontextDataSource dataSource, params (string MemoryId, int SpanIndex, string SpanText, string EntityId, float Confidence)[] rows
	) {
		var sql =
			"INSERT INTO ldb.main.entity_mentions (memory_id, span_index, span_text, entity_id, confidence, resolved_by, linked_at)\nVALUES "
			+ string.Join(", ", Enumerable.Repeat("(?, ?, ?, ?, ?, ?, ?)", rows.Length));

		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = sql;

			foreach (var row in rows) {
				command.Parameters.Add(new DuckDBParameter(row.MemoryId));
				command.Parameters.Add(new DuckDBParameter(row.SpanIndex));
				command.Parameters.Add(new DuckDBParameter(row.SpanText));
				command.Parameters.Add(new DuckDBParameter(row.EntityId));
				command.Parameters.Add(new DuckDBParameter(row.Confidence));
				command.Parameters.Add(new DuckDBParameter(1));
				command.Parameters.Add(new DuckDBParameter(0L));
			}

			command.ExecuteNonQuery();
		});
	}

	#endregion // Test Infrastructure
}
