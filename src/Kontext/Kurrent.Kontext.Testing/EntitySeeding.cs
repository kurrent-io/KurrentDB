// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// Seeds the entities read model exactly how the entity projector writes it, mirroring
/// <see cref="MemorySeeding"/>: the store is read-only, so suites create the schema through
/// <see cref="KontextSchemaTask"/> and insert rows directly with SQL.
/// </summary>
public static class EntitySeeding {
	public static ValueTask CreateSchema(KontextDataSource dataSource) =>
		new(new KontextSchemaTask().ExecuteAsync(dataSource));

	/// <summary>
	/// Zero-pads a hand-written probe to the schema's FLOAT[N] — cosine over zero-padded vectors
	/// equals cosine over the originals, so suites keep writing legible 4-dim geometry.
	/// </summary>
	public static float[] Embedding(params float[] head) {
		var padded = new float[KontextSchemaTask.Dimension];
		head.CopyTo(padded, 0);
		return padded;
	}

	public static void Insert(KontextDataSource dataSource, params EntitySeed[] entities) =>
		Insert(dataSource, "entities", EntityColumns, entities);

	public static void Insert(KontextDataSource dataSource, params MentionSeed[] mentions) =>
		Insert(dataSource, "entity_mentions", MentionColumns, mentions);

	public static void Insert(KontextDataSource dataSource, params LinkSeed[] links) =>
		Insert(dataSource, "entity_links", LinkColumns, links);

	static void Insert<TRow>(KontextDataSource dataSource, string table, (string Name, Func<TRow, object?> Value)[] columns, TRow[] rows) {
		if (rows.Length == 0)
			return;

		var insertInto = $"INSERT INTO ldb.main.{table} (\n  {string.Join(",\n  ", columns.Select(column => column.Name))})\nVALUES";
		var tuple      = "(" + string.Join(", ", Enumerable.Repeat("?", columns.Length)) + ")";
		var values     = string.Join(",\n", Enumerable.Repeat(tuple, rows.Length));

		// Seeding is writing: one dedicated writer per call — writers hold their connection,
		// they never go through the per-call read surface.
		using var connection = dataSource.OpenLanceWriter();

		using var insert = connection.CreateCommand();
		insert.CommandText = $"{insertInto}\n{values}";

		foreach (var row in rows)
		foreach (var (_, value) in columns)
			insert.Parameters.Add(new DuckDBParameter(value(row) ?? DBNull.Value));

		insert.ExecuteNonQuery();
	}

	static readonly (string Name, Func<EntitySeed, object?> Value)[] EntityColumns = [
		("entity_id",       seed => seed.Id),
		("name",            seed => seed.Name),
		("normalized_name", seed => EntityName.Normalize(seed.Name)),
		("entity_type",     seed => seed.Type),
		("subtype",         seed => seed.Subtype),
		("aliases",         seed => seed.Aliases.Count > 0 ? seed.Aliases : [EntityName.Normalize(seed.Name)]),
		("mention_count",   seed => seed.MentionCount),
		("confidence",      seed => seed.Confidence),
		("first_seen",      seed => seed.FirstSeen.ToUnixTimeMilliseconds()),
		("last_seen",       seed => (seed.LastSeen ?? seed.FirstSeen).ToUnixTimeMilliseconds()),
		("log_position",    seed => seed.LogPosition),
		("embedding",       seed => seed.Embedding),
	];

	static readonly (string Name, Func<MentionSeed, object?> Value)[] MentionColumns = [
		("entity_id",    seed => seed.EntityId),
		("memory_id",    seed => seed.MemoryId),
		("surface",      seed => seed.Surface),
		("start_pos",    seed => seed.StartPos),
		("end_pos",      seed => seed.EndPos),
		("confidence",   seed => seed.Confidence),
		("extractor",    seed => seed.Extractor),
		("retained_at",  seed => seed.RetainedAt.ToUnixTimeMilliseconds()),
		("log_position", seed => seed.LogPosition),
	];

	static readonly (string Name, Func<LinkSeed, object?> Value)[] LinkColumns = [
		("source_entity_id", seed => seed.SourceEntityId),
		("target_entity_id", seed => seed.TargetEntityId),
		("confidence",       seed => seed.Confidence),
		("method",           seed => seed.Method),
		("status",           seed => seed.Status),
		("created_at",       seed => seed.CreatedAt.ToUnixTimeMilliseconds()),
		("log_position",     seed => seed.LogPosition),
	];
}

/// <summary>One entity seed row: the fields the tests set, with neutral defaults for the rest.</summary>
public sealed record EntitySeed(string Id, string Name, string Type, DateTimeOffset FirstSeen) {
	public string          Subtype      { get; init; } = "";
	public List<string>    Aliases      { get; init; } = [];
	public long            MentionCount { get; init; } = 1;
	public double          Confidence   { get; init; } = 1.0;
	public DateTimeOffset? LastSeen     { get; init; }
	public long            LogPosition  { get; init; }

	/// <summary>A neutral unit vector at the schema's dimension; vector suites set real geometry via <see cref="EntitySeeding.Embedding"/>.</summary>
	public float[] Embedding { get; init; } = EntitySeeding.Embedding(1f);
}

/// <summary>One mention seed row.</summary>
public sealed record MentionSeed(string EntityId, string MemoryId, string Surface, DateTimeOffset RetainedAt) {
	public int?   StartPos    { get; init; }
	public int?   EndPos      { get; init; }
	public double Confidence  { get; init; } = 1.0;
	public string Extractor   { get; init; } = "test";
	public long   LogPosition { get; init; }
}

/// <summary>One suspected-duplicate link seed row.</summary>
public sealed record LinkSeed(string SourceEntityId, string TargetEntityId, DateTimeOffset CreatedAt) {
	public double Confidence  { get; init; } = 0.9;
	public string Method      { get; init; } = "semantic";
	public string Status      { get; init; } = "pending";
	public long   LogPosition { get; init; }
}
