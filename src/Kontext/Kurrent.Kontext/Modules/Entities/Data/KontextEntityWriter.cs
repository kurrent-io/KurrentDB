// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Quack;

namespace Kurrent.Kontext.Modules.Entities.Data;

/// <summary>
/// The entities read model's batch writer: executes one computed <see cref="EntityDelta"/> as
/// idempotent MERGEs — it persists what <see cref="KontextEntityProjection"/> decided and
/// decides nothing itself. Runs on the caller's connection; the projector owns the connection,
/// the transaction scope, and the checkpoint. Not thread safe: one consumer loop drives it,
/// batch by batch.
///
/// Replay safety (a crash between an applied batch and its checkpoint replays the whole batch):
/// - entity ids are DETERMINISTIC — a replayed create upserts the same row, never a duplicate
/// - mention and link writes are MERGEs on their natural keys — replays no-op
/// - mention_count is RECOUNTED from the mentions table after the mention merge, never
///   incremented — a replay cannot inflate it
/// - the remaining entity folds are absolute values or monotonic (greatest) — idempotent
/// </summary>
public sealed class KontextEntityWriter(DuckDBAdvancedConnection connection, int dimension) {
	/// <summary>
	/// Applies one delta: mentions first, then the recount those mentions feed, then the entity
	/// upserts carrying the exact counts, then the review links. Safe to replay wholesale.
	/// </summary>
	public void Apply(EntityDelta delta) {
		if (delta.IsEmpty)
			return;

		ApplyMentions(delta.Mentions);

		var mentionCounts = CountMentions([.. delta.Entities.Select(entity => entity.EntityId)]);

		ApplyEntities(delta.Entities, mentionCounts);
		ApplyLinks(delta.Links);
	}

	void ApplyMentions(IReadOnlyList<MentionWrite> mentions) {
		// The MERGE source must be duplicate-free on the match key. Extraction can legitimately
		// repeat a surface at the same position across stages; the pipeline already merged
		// those, so remaining repeats are same-surface-same-memory without positions.
		var distinct = mentions.DistinctBy(mention => (mention.EntityId, mention.MemoryId, mention.Surface, mention.StartPos ?? -1)).ToList();

		if (distinct.Count == 0)
			return;

		const string sql =
			"""
			MERGE INTO ldb.main.entity_mentions AS t
			USING (SELECT
			    unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
			    unnest(CAST($memory_ids AS VARCHAR[])) AS memory_id,
			    unnest(CAST($surfaces AS VARCHAR[])) AS surface,
			    unnest(CAST($start_positions AS INTEGER[])) AS start_pos,
			    unnest(CAST($end_positions AS INTEGER[])) AS end_pos,
			    unnest(CAST($confidences AS DOUBLE[])) AS confidence,
			    unnest(CAST($extractors AS VARCHAR[])) AS extractor,
			    unnest(CAST($retained_ats AS BIGINT[])) AS retained_at,
			    unnest(CAST($log_positions AS BIGINT[])) AS log_position) AS s
			ON t.entity_id = s.entity_id
			   AND t.memory_id = s.memory_id
			   AND t.surface = s.surface
			   AND coalesce(t.start_pos, -1) = coalesce(s.start_pos, -1)
			WHEN NOT MATCHED THEN INSERT (
			    entity_id, memory_id, surface, start_pos, end_pos, confidence, extractor, retained_at, log_position)
			VALUES (
			    s.entity_id, s.memory_id, s.surface, s.start_pos, s.end_pos, s.confidence, s.extractor, s.retained_at, s.log_position)
			""";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("entity_ids", distinct.Select(mention => mention.EntityId).ToList()));
		command.Parameters.Add(new DuckDBParameter("memory_ids", distinct.Select(mention => mention.MemoryId).ToList()));
		command.Parameters.Add(new DuckDBParameter("surfaces", distinct.Select(mention => mention.Surface).ToList()));
		command.Parameters.Add(new DuckDBParameter("start_positions", distinct.Select(mention => mention.StartPos).ToList()));
		command.Parameters.Add(new DuckDBParameter("end_positions", distinct.Select(mention => mention.EndPos).ToList()));
		command.Parameters.Add(new DuckDBParameter("confidences", distinct.Select(mention => mention.Confidence).ToList()));
		command.Parameters.Add(new DuckDBParameter("extractors", distinct.Select(mention => mention.Extractor).ToList()));
		command.Parameters.Add(new DuckDBParameter("retained_ats", distinct.Select(mention => mention.RetainedAt).ToList()));
		command.Parameters.Add(new DuckDBParameter("log_positions", distinct.Select(mention => mention.LogPosition).ToList()));
		command.ExecuteNonQuery();
	}

	// Recounted, never incremented: lance commits per statement, so the mention merge above is
	// visible to this same connection — the count is exact even under replay.
	Dictionary<string, long> CountMentions(List<string> entityIds) {
		if (entityIds.Count == 0)
			return [];

		const string sql =
			"""
			SELECT entity_id, count(*)
			FROM ldb.main.entity_mentions
			WHERE array_contains(CAST($entity_ids AS VARCHAR[]), entity_id)
			GROUP BY entity_id
			""";

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("entity_ids", entityIds));

		var       counts = new Dictionary<string, long>();
		using var reader = command.ExecuteReader();

		while (reader.Read())
			counts[reader.GetString(0)] = reader.GetInt64(1);

		return counts;
	}

	// Upsert by deterministic entity id. The matched arm never touches identity columns (name,
	// normalized name, type, subtype, first_seen, embedding) — merges add aliases and recency,
	// they do not rename. Aliases and mention_count are ABSOLUTE values computed this batch;
	// confidence and last_seen fold monotonically. All idempotent under replay. The embedding
	// cast rides a CASE because folded-onto-existing rows carry an empty vector the insert arm
	// must never see (the memory writer's retain-guard rule, entity edition).
	void ApplyEntities(IReadOnlyCollection<EntityWrite> entities, Dictionary<string, long> mentionCounts) {
		if (entities.Count == 0)
			return;

		var sql =
			$"""
			 MERGE INTO ldb.main.entities AS t
			 USING (SELECT
			     unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
			     unnest(CAST($names AS VARCHAR[])) AS name,
			     unnest(CAST($normalized_names AS VARCHAR[])) AS normalized_name,
			     unnest(CAST($entity_types AS VARCHAR[])) AS entity_type,
			     unnest(CAST($subtypes AS VARCHAR[])) AS subtype,
			     unnest(CAST($aliases AS VARCHAR[][])) AS aliases,
			     unnest(CAST($mention_counts AS BIGINT[])) AS mention_count,
			     unnest(CAST($confidences AS DOUBLE[])) AS confidence,
			     unnest(CAST($first_seens AS BIGINT[])) AS first_seen,
			     unnest(CAST($last_seens AS BIGINT[])) AS last_seen,
			     unnest(CAST($log_positions AS BIGINT[])) AS log_position,
			     unnest(CAST($embeddings AS FLOAT[][])) AS embedding_raw) AS s
			 ON t.entity_id = s.entity_id
			 WHEN NOT MATCHED THEN INSERT (
			     entity_id, name, normalized_name, entity_type, subtype, aliases,
			     mention_count, confidence, first_seen, last_seen, log_position, embedding)
			 VALUES (
			     s.entity_id, s.name, s.normalized_name, s.entity_type, s.subtype, s.aliases,
			     s.mention_count, s.confidence, s.first_seen, s.last_seen, s.log_position,
			     CASE WHEN len(s.embedding_raw) > 0 THEN CAST(s.embedding_raw AS FLOAT[{dimension}]) END)
			 WHEN MATCHED THEN UPDATE SET
			     aliases       = s.aliases
			   , mention_count = s.mention_count
			   , confidence    = greatest(t.confidence, s.confidence)
			   , last_seen     = greatest(t.last_seen, s.last_seen)
			   , log_position  = s.log_position
			 """;

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("entity_ids", entities.Select(entity => entity.EntityId).ToList()));
		command.Parameters.Add(new DuckDBParameter("names", entities.Select(entity => entity.Name).ToList()));
		command.Parameters.Add(new DuckDBParameter("normalized_names", entities.Select(entity => entity.NormalizedName).ToList()));
		command.Parameters.Add(new DuckDBParameter("entity_types", entities.Select(entity => entity.EntityType).ToList()));
		command.Parameters.Add(new DuckDBParameter("subtypes", entities.Select(entity => entity.Subtype).ToList()));
		command.Parameters.Add(new DuckDBParameter("aliases", entities.Select(entity => entity.Aliases.ToList()).ToList()));
		command.Parameters.Add(new DuckDBParameter("mention_counts", entities.Select(entity => mentionCounts.GetValueOrDefault(entity.EntityId)).ToList()));
		command.Parameters.Add(new DuckDBParameter("confidences", entities.Select(entity => entity.Confidence).ToList()));
		command.Parameters.Add(new DuckDBParameter("first_seens", entities.Select(entity => entity.FirstSeen).ToList()));
		command.Parameters.Add(new DuckDBParameter("last_seens", entities.Select(entity => entity.LastSeen).ToList()));
		command.Parameters.Add(new DuckDBParameter("log_positions", entities.Select(entity => entity.LogPosition).ToList()));
		command.Parameters.Add(new DuckDBParameter("embeddings", entities.Select(entity => entity.Embedding).ToList()));
		command.ExecuteNonQuery();
	}

	void ApplyLinks(IReadOnlyList<LinkWrite> links) {
		if (links.Count == 0)
			return;

		// status seeds 'pending' at birth and is never touched on replay — a review decision
		// (confirmed/rejected) must survive the batch that created the link replaying.
		const string sql =
			"""
			MERGE INTO ldb.main.entity_links AS t
			USING (SELECT
			    unnest(CAST($source_ids AS VARCHAR[])) AS source_entity_id,
			    unnest(CAST($target_ids AS VARCHAR[])) AS target_entity_id,
			    unnest(CAST($confidences AS DOUBLE[])) AS confidence,
			    unnest(CAST($methods AS VARCHAR[])) AS method,
			    unnest(CAST($created_ats AS BIGINT[])) AS created_at,
			    unnest(CAST($log_positions AS BIGINT[])) AS log_position) AS s
			ON t.source_entity_id = s.source_entity_id AND t.target_entity_id = s.target_entity_id
			WHEN NOT MATCHED THEN INSERT (
			    source_entity_id, target_entity_id, confidence, method, status, created_at, log_position)
			VALUES (
			    s.source_entity_id, s.target_entity_id, s.confidence, s.method, 'pending', s.created_at, s.log_position)
			""";

		var distinct = links.DistinctBy(link => (link.SourceEntityId, link.TargetEntityId)).ToList();

		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.Add(new DuckDBParameter("source_ids", distinct.Select(link => link.SourceEntityId).ToList()));
		command.Parameters.Add(new DuckDBParameter("target_ids", distinct.Select(link => link.TargetEntityId).ToList()));
		command.Parameters.Add(new DuckDBParameter("confidences", distinct.Select(link => link.Confidence).ToList()));
		command.Parameters.Add(new DuckDBParameter("methods", distinct.Select(link => link.Method).ToList()));
		command.Parameters.Add(new DuckDBParameter("created_ats", distinct.Select(link => link.CreatedAt).ToList()));
		command.Parameters.Add(new DuckDBParameter("log_positions", distinct.Select(link => link.LogPosition).ToList()));
		command.ExecuteNonQuery();
	}
}
