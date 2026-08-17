// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Data.Common;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Quack;

namespace Kurrent.Kontext.Modules.Entities.Data;

/// <summary>One row of the entities read model, as the resolvers and dedup policy consume it.</summary>
public sealed record EntityRow {
	public required string EntityId       { get; init; }
	public required string Name           { get; init; }
	public required string NormalizedName { get; init; }
	public required string EntityType     { get; init; }

	/// <summary>Empty string when the entity has no finer classification.</summary>
	public string Subtype { get; init; } = "";

	/// <summary>Normalized surface forms this entity is known by, the canonical one included.</summary>
	public IReadOnlyList<string> Aliases { get; init; } = [];

	public long   MentionCount { get; init; }
	public double Confidence   { get; init; }
	public long   FirstSeen    { get; init; }
	public long   LastSeen     { get; init; }
	public long   LogPosition  { get; init; }
}

/// <summary>One provenance row: where an entity surfaced, verbatim, and which stage saw it.</summary>
public sealed record EntityMentionRow {
	public required string EntityId { get; init; }
	public required string MemoryId { get; init; }
	public required string Surface  { get; init; }

	public int?   StartPos   { get; init; }
	public int?   EndPos     { get; init; }
	public double Confidence { get; init; }
	public string Extractor  { get; init; } = "";
	public long   RetainedAt { get; init; }
}

/// <summary>One suspected-duplicate pair awaiting review — the SAME_AS ledger's row.</summary>
public sealed record EntityLinkRow {
	public required string SourceEntityId { get; init; }
	public required string TargetEntityId { get; init; }

	public double Confidence { get; init; }
	public string Method     { get; init; } = "";
	public string Status     { get; init; } = "";
	public long   CreatedAt  { get; init; }
}

/// <summary>An entity candidate scored by embedding similarity to a probe vector.</summary>
public sealed record ScoredEntity(EntityRow Entity, double CosineSimilarity);

/// <summary>
/// The entities read model's read surface — resolution's candidate source and the review UI's
/// query layer. Read-only by design: the projector owns every write, mirroring
/// <see cref="Kurrent.Kontext.Data.KontextDataStore"/>.
/// <para>Two constructions, one non-negotiable rule: an attached lance catalog serves each
/// connection the dataset view it FIRST scanned (validated live — a connection used before a
/// write never sees that write). Ordinary readers take the data source, which leases a fresh
/// connection per call; the projector's resolution MUST take the projector's own write
/// connection, the one surface guaranteed to see every batch it already applied.</para>
/// </summary>
public sealed class KontextEntityStore {
	const string EntityColumns =
		"entity_id, name, normalized_name, entity_type, subtype, aliases, mention_count, confidence, first_seen, last_seen, log_position";

	const string MentionColumns =
		"entity_id, memory_id, surface, start_pos, end_pos, confidence, extractor, retained_at";

	const string LinkColumns =
		"source_entity_id, target_entity_id, confidence, method, status, created_at";

	readonly KontextDataSource?        _dataSource;
	readonly DuckDBAdvancedConnection? _connection;

	/// <summary>The general read surface: fresh leased connections.</summary>
	public KontextEntityStore(KontextDataSource dataSource) => _dataSource = dataSource;

	/// <summary>The projector's read surface: its own write connection, which sees its own writes.</summary>
	public KontextEntityStore(DuckDBAdvancedConnection connection) => _connection = connection;

	Task<T> ExecuteAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken ct) {
		if (_dataSource is not null)
			return _dataSource.ExecuteAsync(operation, ct).AsTask();

		// Inline on the caller's thread, like the data source's own read surface — DuckDB has no
		// true async API. Faults surface through the returned task.
		try {
			ct.ThrowIfCancellationRequested();
			return Task.FromResult(operation(_connection!));
		} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
			return Task.FromCanceled<T>(ct);
		} catch (Exception ex) {
			return Task.FromException<T>(ex);
		}
	}

	public Task<EntityRow?> GetAsync(string entityId, CancellationToken ct = default) {
		const string commandText =
			$"""
			 SELECT {EntityColumns}
			 FROM ldb.main.entities
			 WHERE entity_id = $entity_id
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("entity_id", entityId));

				using var reader = command.ExecuteReader();
				return reader.Read() ? ReadEntity(reader) : null;
			}, ct);
	}

	/// <summary>
	/// The exact-resolution probe: the entity of this type whose normalized name OR any alias
	/// equals <paramref name="normalizedName"/>. At most one row can match — the projector only
	/// ever creates an entity after this probe missed.
	/// </summary>
	public Task<EntityRow?> FindExactAsync(string normalizedName, string entityType, CancellationToken ct = default) {
		const string commandText =
			$"""
			 SELECT {EntityColumns}
			 FROM ldb.main.entities
			 WHERE entity_type = $entity_type
			   AND (normalized_name = $normalized_name OR array_contains(aliases, $normalized_name))
			 LIMIT 1
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("entity_type", entityType));
				command.Parameters.Add(new("normalized_name", normalizedName));

				using var reader = command.ExecuteReader();
				return reader.Read() ? ReadEntity(reader) : null;
			}, ct);
	}

	/// <summary>
	/// The read path's entity recognition: every entity whose normalized name OR any alias is one
	/// of <paramref name="surfaces"/>, across ALL types. Type-blind on purpose — a question does not
	/// say whether "lovelace" is the person or the street, so both come back and the ranking settles
	/// it. Surfaces must already be normalized (the caller shares <c>EntityName</c>'s rule, which is
	/// also the key <c>aliases</c> is stored under).
	/// </summary>
	public Task<List<EntityRow>> MatchBySurfacesAsync(IReadOnlyCollection<string> surfaces, CancellationToken ct = default) {
		if (surfaces.Count == 0)
			return Task.FromResult(new List<EntityRow>());

		// Both halves of the OR are needed even though aliases carries the canonical name too: an
		// entity written before an alias set existed still matches on its normalized name alone.
		const string commandText =
			$"""
			 SELECT {EntityColumns}
			 FROM ldb.main.entities
			 WHERE array_contains(CAST($surfaces AS VARCHAR[]), normalized_name)
			    OR list_has_any(aliases, CAST($surfaces AS VARCHAR[]))
			 ORDER BY mention_count DESC, entity_id
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("surfaces", surfaces.ToArray()));

				return ReadEntities(command);
			}, ct);
	}

	/// <summary>
	/// The fuzzy-resolution candidate pool: every entity of one type. Bounded by
	/// <paramref name="limit"/> as a safety rail — fuzzy scoring happens in process, and a
	/// pathological type population must cap, not stall, the projector.
	/// </summary>
	public Task<List<EntityRow>> ListByTypeAsync(string entityType, int limit, CancellationToken ct = default) {
		const string commandText =
			$"""
			 SELECT {EntityColumns}
			 FROM ldb.main.entities
			 WHERE entity_type = $entity_type
			 ORDER BY mention_count DESC, entity_id
			 LIMIT $limit
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("entity_type", entityType));
				command.Parameters.Add(new("limit", limit));

				return ReadEntities(command);
			}, ct);
	}

	/// <summary>
	/// The semantic-resolution probe: the <paramref name="k"/> entities of one type nearest to
	/// the probe embedding, best first. An exact scan on purpose (array_cosine_similarity over
	/// the type's rows) — correct at any size, no index lifecycle, and the entity population of
	/// a node-local read model stays well inside scan territory.
	/// </summary>
	public Task<List<ScoredEntity>> SearchSimilarAsync(float[] embedding, string entityType, int k, CancellationToken ct = default) {
		// The FLOAT[N] cast is the one thing that can never be a parameter — a type, not a value.
		var commandText =
			$"""
			 SELECT {EntityColumns},
			        array_cosine_similarity(embedding, CAST($embedding AS FLOAT[{embedding.Length}])) AS cosine
			 FROM ldb.main.entities
			 WHERE entity_type = $entity_type
			 ORDER BY cosine DESC
			 LIMIT $k
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("embedding", embedding));
				command.Parameters.Add(new("entity_type", entityType));
				command.Parameters.Add(new("k", k));

				var       results = new List<ScoredEntity>();
				using var reader  = command.ExecuteReader();

				while (reader.Read())
					results.Add(new(ReadEntity(reader), Convert.ToDouble(reader.GetValue(11))));

				return results;
			}, ct);
	}

	/// <summary>Every entity mentioned by one memory — the "what did this memory talk about" walk.</summary>
	public Task<List<EntityMentionRow>> ListMentionsOfMemoryAsync(string memoryId, CancellationToken ct = default) =>
		ListMentions("memory_id", memoryId, ct);

	/// <summary>Every memory that mentioned one entity — the provenance walk behind entity linking.</summary>
	public Task<List<EntityMentionRow>> ListMentionsOfEntityAsync(string entityId, CancellationToken ct = default) =>
		ListMentions("entity_id", entityId, ct);

	/// <summary>
	/// Every memory that mentioned ANY of <paramref name="entityIds"/> — the read path's provenance
	/// walk, one query for the whole matched set instead of one per entity.
	/// </summary>
	public Task<List<EntityMentionRow>> ListMentionsOfEntitiesAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default) {
		if (entityIds.Count == 0)
			return Task.FromResult(new List<EntityMentionRow>());

		const string commandText =
			$"""
			 SELECT {MentionColumns}
			 FROM ldb.main.entity_mentions
			 WHERE array_contains(CAST($entity_ids AS VARCHAR[]), entity_id)
			 ORDER BY retained_at, entity_id, memory_id
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("entity_ids", entityIds.ToArray()));

				return ReadMentions(command);
			}, ct);
	}

	/// <summary>The suspected-duplicate pairs carrying one status, oldest first — the review queue.</summary>
	public Task<List<EntityLinkRow>> ListLinksAsync(string status, int limit, CancellationToken ct = default) {
		const string commandText =
			$"""
			 SELECT {LinkColumns}
			 FROM ldb.main.entity_links
			 WHERE status = $status
			 ORDER BY created_at, source_entity_id
			 LIMIT $limit
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("status", status));
				command.Parameters.Add(new("limit", limit));

				return ReadLinks(command);
			}, ct);
	}

	/// <summary>
	/// The links of one status touching any of <paramref name="entityIds"/>, in EITHER direction —
	/// the read path's one-hop neighbourhood, oldest first. Unlimited on purpose: the hop starts
	/// from the entities one question named, and the decision rule only writes a link when a name
	/// landed inside the doubt band, so this is a handful of rows — a silent cap here would drop a
	/// doubt the read path is meant to price in.
	/// </summary>
	public Task<List<EntityLinkRow>> ListLinksTouchingAsync(IReadOnlyCollection<string> entityIds, string status, CancellationToken ct = default) {
		if (entityIds.Count == 0)
			return Task.FromResult(new List<EntityLinkRow>());

		const string commandText =
			$"""
			 SELECT {LinkColumns}
			 FROM ldb.main.entity_links
			 WHERE status = $status
			   AND (array_contains(CAST($entity_ids AS VARCHAR[]), source_entity_id)
			     OR array_contains(CAST($entity_ids AS VARCHAR[]), target_entity_id))
			 ORDER BY created_at, source_entity_id
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("status", status));
				command.Parameters.Add(new("entity_ids", entityIds.ToArray()));

				return ReadLinks(command);
			}, ct);
	}

	/// <summary>
	/// Whether all three entity tables exist yet — the quiet-skip probe for READ paths that can run
	/// before the projector bootstrapped its schema. The read path must degrade to "no entities
	/// known", never fail a retrieval because the read model is still being built.
	/// </summary>
	public Task<bool> ExistsAsync(CancellationToken ct = default) {
		const string commandText =
			"""
			SELECT count(*)
			FROM duckdb_tables()
			WHERE database_name = 'ldb'
			  AND table_name IN ('entities', 'entity_mentions', 'entity_links')
			""";

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				return (long)command.ExecuteScalar()! == 3;
			}, ct);
	}

	public Task<long> CountAsync(CancellationToken ct = default) =>
		ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT count(*) FROM ldb.main.entities";
				return (long)command.ExecuteScalar()!;
			}, ct);

	Task<List<EntityMentionRow>> ListMentions(string keyColumn, string keyValue, CancellationToken ct) {
		// keyColumn is one of two constants chosen above — never caller input.
		var commandText =
			$"""
			 SELECT {MentionColumns}
			 FROM ldb.main.entity_mentions
			 WHERE {keyColumn} = $key
			 ORDER BY retained_at, entity_id, memory_id
			 """;

		return ExecuteAsync(
			connection => {
				using var command = connection.CreateCommand();
				command.CommandText = commandText;
				command.Parameters.Add(new("key", keyValue));

				return ReadMentions(command);
			}, ct);
	}

	// Reads POSITIONALLY in MentionColumns order.
	static List<EntityMentionRow> ReadMentions(DbCommand command) {
		var       mentions = new List<EntityMentionRow>();
		using var reader   = command.ExecuteReader();

		while (reader.Read())
			mentions.Add(new() {
				EntityId   = reader.GetString(0),
				MemoryId   = reader.GetString(1),
				Surface    = reader.GetString(2),
				StartPos   = reader.IsDBNull(3) ? null : reader.GetInt32(3),
				EndPos     = reader.IsDBNull(4) ? null : reader.GetInt32(4),
				Confidence = reader.GetDouble(5),
				Extractor  = reader.GetString(6),
				RetainedAt = reader.GetInt64(7),
			});

		return mentions;
	}

	// Reads POSITIONALLY in LinkColumns order.
	static List<EntityLinkRow> ReadLinks(DbCommand command) {
		var       links  = new List<EntityLinkRow>();
		using var reader = command.ExecuteReader();

		while (reader.Read())
			links.Add(new() {
				SourceEntityId = reader.GetString(0),
				TargetEntityId = reader.GetString(1),
				Confidence     = reader.GetDouble(2),
				Method         = reader.GetString(3),
				Status         = reader.GetString(4),
				CreatedAt      = reader.GetInt64(5),
			});

		return links;
	}

	static List<EntityRow> ReadEntities(DbCommand command) {
		var       entities = new List<EntityRow>();
		using var reader   = command.ExecuteReader();

		while (reader.Read())
			entities.Add(ReadEntity(reader));

		return entities;
	}

	// Reads one row POSITIONALLY in EntityColumns order, off the validated wire shapes:
	// VARCHAR[] arrives as List<string>.
	static EntityRow ReadEntity(DbDataReader reader) => new() {
		EntityId       = reader.GetString(0),
		Name           = reader.GetString(1),
		NormalizedName = reader.GetString(2),
		EntityType     = reader.GetString(3),
		Subtype        = reader.GetString(4),
		Aliases        = [.. (IEnumerable<string>)reader.GetValue(5)],
		MentionCount   = reader.GetInt64(6),
		Confidence     = reader.GetDouble(7),
		FirstSeen      = reader.GetInt64(8),
		LastSeen       = reader.GetInt64(9),
		LogPosition    = reader.GetInt64(10),
	};
}
