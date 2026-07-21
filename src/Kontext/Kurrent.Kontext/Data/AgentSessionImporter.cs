// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Copies Claude Code agent session data into plain relational tables using the DuckDB
/// <c>agent_data</c> community extension. One owner for every statement, exactly like
/// <see cref="KontextSchema"/>: the class holds the whole idempotent per-tick import batch and the
/// count probes callers need, and nothing else composes SQL against these tables.
///
/// Two tables split ONE materialized scan per tick (the extension re-reads every JSONL file on each
/// call — no pushdown — so scanning once and splitting it halves the cost):
/// - <c>sessions.messages</c> — the parseable conversation rows, appended incrementally (anti-join on
///   <c>uuid</c>). These are the RAW session tables in the engine's own catalog, NOT the <c>ldb</c>
///   Lance namespace — Lance is the vector side; plain session history is plain DuckDB SQL.
/// - <c>sessions.parse_errors</c> — the lines this extension build cannot parse yet, KEPT rather than
///   dropped, with SNAPSHOT semantics (<c>CREATE OR REPLACE</c> every tick). The table therefore
///   always means "lines the current build cannot parse": when a newer build learns a line type,
///   those lines graduate into <c>sessions.messages</c> and drop out of the snapshot on the next tick.
///
/// The two tables are linked by a LOGICAL join on <c>session_id</c> (with <c>file_name</c>/
/// <c>line_number</c> pinning the source line), NOT an enforced FOREIGN KEY: <c>messages.session_id</c>
/// is not unique (a real FK would need a separate sessions dimension table), and sessions made up
/// ENTIRELY of unparseable lines exist — an enforced FK would reject exactly the rows we are keeping.
///
/// Renting from the pool's READ surface (<see cref="KontextConnectionPool.ExecuteAsync{T}"/>) is
/// deliberate. The writer discipline — a dedicated connection, commit-conflict retry, prepared
/// statement reuse — exists for Lance commits; none of it applies to a plain-table INSERT, and the
/// import scheduler's tick gate already serializes ticks, so renting keeps the importer stateless.
/// </summary>
public sealed class AgentSessionImporter(KontextConnectionPool connections, AgentSessionImportOptions options) {
    // The probe that keeps the Count* methods tolerant of the very first tick: duckdb_tables() lists
    // across every attached catalog and never throws, so it answers "does the table exist yet" without
    // touching a table that may not be there. schema_name/table_name (not database_name) because the
    // sessions schema lives in the engine's own catalog, whose name is the engine file's stem.
    const string TableExistsSql =
        """
        SELECT count(*)
        FROM duckdb_tables()
        WHERE schema_name = 'sessions'
          AND table_name = $table_name
        """;

    // Composed ONCE at construction, never per call: the source path cannot be a bound parameter
    // (read_conversations takes it as a SQL literal, the same rule that embeds KontextSchema's
    // dataset path), so it is resolved and quote-escaped here rather than re-escaped every tick.
    readonly string _importSql = BuildImportSql(options.SourcePath);

    /// <summary>
    /// Runs the one idempotent per-tick import batch: load the extension, ensure the schema and
    /// messages table, materialize a single scan, append the new message tail from it, and rebuild
    /// the parse-error snapshot from the same scan. Self-sufficient — it creates everything it needs,
    /// so it takes no existence probe of its own; a caller can invoke it against a fresh pool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The batch is one command (multiple statements, one CommandText): INSTALL/LOAD are no-ops once
    /// the extension is present — the first INSTALL needs network, later ones hit the ~/.duckdb cache;
    /// an offline first tick therefore fails loudly rather than half-importing, and the scheduler's
    /// cadence is the retry.
    /// </para>
    /// <para>
    /// The scan is materialized into a <c>CREATE OR REPLACE TEMP TABLE</c> — OR REPLACE because pooled
    /// connections outlive ticks, so a tick that failed after creating the temp table may have left it
    /// behind. The messages DDL is explicit (all 27 extension columns plus <c>imported_at</c>) on
    /// purpose: an extension upgrade that adds columns must not silently reshape our table. The
    /// incremental key is <c>uuid</c> (message identity); forked or resumed sessions copy prefix
    /// messages into new files, so the incoming scan is deduped by uuid before the ANTI JOIN drops
    /// everything already stored. The parse-error table is <c>CREATE OR REPLACE</c>d whole — it is a
    /// snapshot, not a log.
    /// </para>
    /// </remarks>
    public Task ImportAsync(CancellationToken ct = default) =>
        connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = _importSql;
                command.ExecuteNonQuery();
            }, ct);

    /// <summary>
    /// The total imported message count — the probe tests and callers read state through. Tolerant of
    /// a tick that fires before the first <see cref="ImportAsync"/> has created the table: returns 0
    /// instead of failing.
    /// </summary>
    public Task<long> CountAsync(CancellationToken ct = default) => CountTableAsync("messages", ct);

    /// <summary>
    /// The current parse-error snapshot count — the lines this extension build cannot parse yet.
    /// Mirrors <see cref="CountAsync"/>, including the tolerance of a table that does not exist before
    /// the first import.
    /// </summary>
    public Task<long> CountParseErrorsAsync(CancellationToken ct = default) => CountTableAsync("parse_errors", ct);

    Task<long> CountTableAsync(string tableName, CancellationToken ct) =>
        connections.ExecuteAsync(
            connection => {
                // A bare SELECT count(*) FROM sessions.<table> binds the table name at PARSE time, so
                // it throws before the first import creates the table rather than returning 0. Probe
                // first, count only when the table is really there — two commands on one rented
                // connection because a single batch would still fail to parse its second statement.
                using var probe = connection.CreateCommand();
                probe.CommandText = TableExistsSql;
                probe.Parameters.Add(new("table_name", tableName));

                if ((long)probe.ExecuteScalar()! == 0)
                    return 0L;

                using var count = connection.CreateCommand();
                // A table name cannot be a bound parameter, but tableName is a hardcoded literal here
                // (never caller input), so embedding it carries no injection surface.
                count.CommandText = $"SELECT count(*) FROM sessions.{tableName}";

                return (long)count.ExecuteScalar()!;
            }, ct);

    // Resolve the path in .NET (never rely on duckdb '~' expansion — the server process HOME is not
    // the agent's) and quote-escape it, then embed it in the verbatim spike batch. BOTH named args to
    // read_conversations are required: a single positional arg is a binder error on this build.
    static string BuildImportSql(string sourcePath) {
        var escapedPath = Path.GetFullPath(sourcePath).Replace("'", "''", StringComparison.Ordinal);

        return
            $"""
             INSTALL agent_data FROM community;
             LOAD agent_data;

             CREATE SCHEMA IF NOT EXISTS sessions;

             CREATE TABLE IF NOT EXISTS sessions.messages (
               source                VARCHAR,
               session_id            VARCHAR,
               project_path          VARCHAR,
               project_dir           VARCHAR,
               file_name             VARCHAR,
               is_agent              BOOLEAN,
               line_number           BIGINT,
               message_type          VARCHAR,
               uuid                  VARCHAR,
               parent_uuid           VARCHAR,
               "timestamp"           TIMESTAMPTZ,
               message_role          VARCHAR,
               message_content       VARCHAR,
               model                 VARCHAR,
               tool_name             VARCHAR,
               tool_use_id           VARCHAR,
               tool_input            VARCHAR,
               input_tokens          BIGINT,
               output_tokens         BIGINT,
               cache_creation_tokens BIGINT,
               cache_read_tokens     BIGINT,
               slug                  VARCHAR,
               git_branch            VARCHAR,
               cwd                   VARCHAR,
               version               VARCHAR,
               stop_reason           VARCHAR,
               repository            VARCHAR,
               imported_at           TIMESTAMPTZ DEFAULT now()
             );

             CREATE OR REPLACE TEMP TABLE agent_scan AS
             SELECT * FROM read_conversations(path := '{escapedPath}', source := 'claude');

             INSERT INTO sessions.messages BY NAME
             SELECT
               rc.source, rc.session_id, rc.project_path, rc.project_dir, rc.file_name,
               rc.is_agent, rc.line_number, rc.message_type, rc.uuid, rc.parent_uuid,
               try_cast(rc.timestamp AS TIMESTAMPTZ) AS "timestamp",
               rc.message_role, rc.message_content, rc.model, rc.tool_name, rc.tool_use_id,
               rc.tool_input, rc.input_tokens, rc.output_tokens, rc.cache_creation_tokens,
               rc.cache_read_tokens, rc.slug, rc.git_branch, rc.cwd, rc.version,
               rc.stop_reason, rc.repository
             FROM agent_scan AS rc
             ANTI JOIN sessions.messages AS m ON rc.uuid = m.uuid
             WHERE rc.message_type <> '_parse_error'
               AND rc.uuid IS NOT NULL
             QUALIFY row_number() OVER (PARTITION BY rc.uuid ORDER BY rc.file_name, rc.line_number) = 1;

             CREATE OR REPLACE TABLE sessions.parse_errors AS
             SELECT
               rc.source, rc.session_id, rc.project_path, rc.project_dir, rc.file_name,
               rc.line_number, rc.message_content AS error, now() AS scanned_at
             FROM agent_scan AS rc
             WHERE rc.message_type = '_parse_error';

             DROP TABLE agent_scan;
             """;
    }
}
