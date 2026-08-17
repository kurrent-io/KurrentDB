// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data.Migrations;
using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckDB;
using Kurrent.Quack;

namespace Kurrent.Kontext.Data;

// The Kontext store's schema — the migration stream, one step class per change.
// New steps are appended to THIS file and registered in KontextSchemaBootstrap.

/// <summary>
/// v1 of the Kontext store: five lance tables and their eager indexes. <c>memories</c> and
/// <c>records</c> are the retrieval surface; the entities read model is <c>entities</c>
/// (one row per resolved entity), <c>entity_mentions</c> (the append-only provenance), and
/// <c>entity_links</c> (the suspected-duplicate pairs awaiting review — the SAME_AS ledger).
///
/// Vector indexes are absent by design, for two different reasons. On memories and records
/// they have a training floor, so they cannot exist on a fresh store —
/// <see cref="KontextIndexMaintenance"/> and the records indexer own their lifecycle. On
/// entities there is none on purpose: entity similarity search runs as an exact scan
/// (array_cosine_similarity), which is correct at any size and fast at the entity counts a
/// node-local read model sees. The day that stops being true, the memories table's lazy
/// IVF_HNSW_PQ lifecycle is the template.
///
/// Idempotent despite RunOnce: journal recording is at-least-once, and stores deployed
/// before the migration stream existed already carry these tables over an empty journal.
///
/// Conventions:
/// - timestamps are BIGINT Unix epoch MILLISECONDS (UTC)
/// - <c>aliases</c> holds NORMALIZED surface forms (lowercase, collapsed whitespace)
///   including the canonical one, so alias containment and normalized-name equality speak
///   the same key
/// - <c>subtype</c> uses '' for none — empty-string sentinels over NULLs on VARCHARs
///
/// Addressing rules (the lance extension's dual addressing):
/// - table DDL uses the qualified name (ldb.main.* — hardcoded, matching store and writer)
/// - index DDL uses the RAW dataset path, and inside WITH (...) it is always '=', never ':='
/// </summary>
public sealed class KontextSchemaTask : IMigrationStep<IDuckDBSchemaExecutor> {
    /// <summary>
    /// The embedding dimension — the N in the FLOAT[N] column type, and the dimension of the
    /// shipped ONNX models (all-MiniLM, multilingual-e5-small). The bootstrap probe fails
    /// startup when the configured model does not produce exactly this dimension — a mismatch
    /// poisons every stored vector.
    /// </summary>
    public const int Dimension = 384;

    public int Version => 1;

    // The version-retention policy v1 ships — frozen with the step; changing it for an
    // existing store is a new step, or the runtime knob (SetAutoCleanup).
    const int    CleanupIntervalCommits = 1000;
    const string CleanupOlderThan       = "1h";
    const int    CleanupRetainVersions  = 3;

    public Task ExecuteAsync(IDuckDBSchemaExecutor executor, CancellationToken ct = default) {
        var script =
            $"""
            CREATE TABLE IF NOT EXISTS ldb.main.memories (
              memory_id        VARCHAR,
              memory_type      INTEGER,
              content          VARCHAR,
              importance       INTEGER,
              tags             VARCHAR[],
              reasoning        VARCHAR,
              evidence         VARCHAR[],
              cited_memory_ids VARCHAR[],
              supersedes       VARCHAR[],
              validity_start   BIGINT,
              validity_end     BIGINT,
              retained_at      BIGINT,
              last_accessed_at BIGINT,
              is_retracted     BOOLEAN,
              retracted_at     BIGINT,
              is_superseded    BOOLEAN,
              superseded_at    BIGINT,
              superseded_by    VARCHAR,
              log_position     BIGINT,
              embedding        FLOAT[{Dimension}]);

            CREATE INDEX memory_id_idx        ON ldb.main.memories (memory_id)        USING BTREE      WITH (replace = true);
            CREATE INDEX content_fts          ON ldb.main.memories (content)          USING INVERTED   WITH (replace = true, base_tokenizer = 'simple', language = 'English', stem = true);
            CREATE INDEX tags_idx             ON ldb.main.memories (tags)             USING LABEL_LIST WITH (replace = true);
            CREATE INDEX evidence_idx         ON ldb.main.memories (evidence)         USING LABEL_LIST WITH (replace = true);
            CREATE INDEX cited_memory_ids_idx ON ldb.main.memories (cited_memory_ids) USING LABEL_LIST WITH (replace = true);
            CREATE INDEX supersedes_idx       ON ldb.main.memories (supersedes)       USING LABEL_LIST WITH (replace = true);
            CREATE INDEX superseded_by_idx    ON ldb.main.memories (superseded_by)    USING BTREE      WITH (replace = true);
            CREATE INDEX log_position_idx     ON ldb.main.memories (log_position)     USING BTREE      WITH (replace = true);
            
            ALTER TABLE ldb.main.memories SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits}, 
                older_than      = '{CleanupOlderThan}', 
                retain_versions = {CleanupRetainVersions}
            );

            CREATE TABLE IF NOT EXISTS ldb.main.records (
                log_position  BIGINT,
                record_id     BLOB,
                stream        VARCHAR,
                category      VARCHAR,
                schema_name   VARCHAR,
                schema_id     VARCHAR,
                schema_format VARCHAR,
                content       VARCHAR,
                created_at    BIGINT,
                embedding     FLOAT[{Dimension}]
            );

            CREATE INDEX log_position_idx ON ldb.main.records (log_position) USING BTREE    WITH (replace = true);
            CREATE INDEX record_id_idx    ON ldb.main.records (record_id)    USING BTREE    WITH (replace = true);
            CREATE INDEX stream_idx       ON ldb.main.records (stream)       USING BTREE    WITH (replace = true);
            CREATE INDEX category_idx     ON ldb.main.records (category)     USING BTREE    WITH (replace = true);
            CREATE INDEX content_fts      ON ldb.main.records (content)      USING INVERTED WITH (replace = true, base_tokenizer = 'simple', language = 'English', stem = true);

            ALTER TABLE ldb.main.records SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits},
                older_than      = '{CleanupOlderThan}',
                retain_versions = {CleanupRetainVersions}
            );

            CREATE TABLE IF NOT EXISTS ldb.main.entities (
              entity_id       VARCHAR,
              name            VARCHAR,
              normalized_name VARCHAR,
              entity_type     VARCHAR,
              subtype         VARCHAR,
              aliases         VARCHAR[],
              mention_count   BIGINT,
              confidence      DOUBLE,
              first_seen      BIGINT,
              last_seen       BIGINT,
              log_position    BIGINT,
              embedding       FLOAT[{Dimension}]);

            CREATE INDEX entity_id_idx       ON ldb.main.entities (entity_id)       USING BTREE      WITH (replace = true);
            CREATE INDEX normalized_name_idx ON ldb.main.entities (normalized_name) USING BTREE      WITH (replace = true);
            CREATE INDEX entity_type_idx     ON ldb.main.entities (entity_type)     USING BTREE      WITH (replace = true);
            CREATE INDEX aliases_idx         ON ldb.main.entities (aliases)         USING LABEL_LIST WITH (replace = true);

            ALTER TABLE ldb.main.entities SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits},
                older_than      = '{CleanupOlderThan}',
                retain_versions = {CleanupRetainVersions}
            );

            CREATE TABLE IF NOT EXISTS ldb.main.entity_mentions (
              entity_id    VARCHAR,
              memory_id    VARCHAR,
              surface      VARCHAR,
              start_pos    INTEGER,
              end_pos      INTEGER,
              confidence   DOUBLE,
              extractor    VARCHAR,
              retained_at  BIGINT,
              log_position BIGINT);

            CREATE INDEX mention_entity_id_idx ON ldb.main.entity_mentions (entity_id) USING BTREE WITH (replace = true);
            CREATE INDEX mention_memory_id_idx ON ldb.main.entity_mentions (memory_id) USING BTREE WITH (replace = true);

            ALTER TABLE ldb.main.entity_mentions SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits},
                older_than      = '{CleanupOlderThan}',
                retain_versions = {CleanupRetainVersions}
            );

            CREATE TABLE IF NOT EXISTS ldb.main.entity_links (
              source_entity_id VARCHAR,
              target_entity_id VARCHAR,
              confidence       DOUBLE,
              method           VARCHAR,
              status           VARCHAR,
              created_at       BIGINT,
              log_position     BIGINT);

            CREATE INDEX link_source_id_idx ON ldb.main.entity_links (source_entity_id) USING BTREE WITH (replace = true);
            CREATE INDEX link_target_id_idx ON ldb.main.entity_links (target_entity_id) USING BTREE WITH (replace = true);
            CREATE INDEX link_status_idx    ON ldb.main.entity_links (status)           USING BTREE WITH (replace = true);

            ALTER TABLE ldb.main.entity_links SET AUTO_CLEANUP WITH (
                interval        = {CleanupIntervalCommits},
                older_than      = '{CleanupOlderThan}',
                retain_versions = {CleanupRetainVersions}
            )
            """;

        return executor.ExecuteAsync(
            connection => connection.ExecuteAdHocNonQuery(script, multipleStatements: true), ct
        ).AsTask();
    }
}
