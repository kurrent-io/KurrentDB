// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data.Migrations;
using Kurrent.Kontext.Infrastructure.Data.Migrations.DuckDB;
using Kurrent.Quack;

namespace Kurrent.Kontext.Data;

// The Kontext store's schema — the migration stream, one step class per change.
// New steps are appended to THIS file and registered in KontextSchemaBootstrap.

/// <summary>
/// v1 of the Kontext store: the memories and records lance tables and their eager indexes.
/// The vector indexes are NOT here — they have a training floor, so they cannot exist on a
/// fresh store; <see cref="KontextIndexMaintenance"/> and the records indexer own their
/// lifecycle.
///
/// Idempotent despite RunOnce: journal recording is at-least-once, and stores deployed
/// before the migration stream existed already carry these tables over an empty journal.
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
            )
            """;
        

        return executor.ExecuteAsync(
            connection => connection.ExecuteAdHocNonQuery(script, multipleStatements: true), ct
        ).AsTask();
    }
}
