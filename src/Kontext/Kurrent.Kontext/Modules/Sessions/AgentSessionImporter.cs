// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Modules.Sessions;

/// <summary>
/// Copies Claude Code agent session data into plain relational tables using the DuckDB
/// <c>agent_data</c> community extension. One owner for every statement, exactly like
/// <see cref="KontextSchema"/>: the class holds the bootstrap DDL, the whole idempotent per-tick
/// import batch, and the count/existence probes callers need, and nothing else composes SQL against
/// these tables.
///
/// The tables live UNQUALIFIED in the engine catalog's <c>main</c> schema, which always exists — so
/// nothing creates a schema. They are ordinary DuckDB tables on the plain bulk-append write path.
///
/// Two tables split ONE materialized scan per tick (the extension re-reads every JSONL file on each
/// call — no pushdown — so scanning once and splitting it halves the cost):
/// - <c>transcripts</c> — the parseable conversation rows, appended incrementally (anti-join on
///   <c>uuid</c>).
/// - <c>transcript_parse_errors</c> — the lines this extension build cannot parse yet, KEPT rather
///   than dropped, with SNAPSHOT semantics (the tick truncates and refills it). The table therefore
///   always means "lines the current build cannot parse": when a newer build learns a line type,
///   those lines graduate into <c>transcripts</c> and drop out of the snapshot on the next tick.
///
/// The two tables are linked by a LOGICAL join on <c>session_id</c> (with <c>file_name</c>/
/// <c>line_number</c> pinning the source line), NOT an enforced FOREIGN KEY: <c>transcripts.session_id</c>
/// is not unique (a real FK would need a separate sessions dimension table), and sessions made up
/// ENTIRELY of unparseable lines exist — an enforced FK would reject exactly the rows we are keeping.
///
/// DDL is a one-time <see cref="CreateAsync"/> bootstrap (mirroring <see cref="KontextSchema.CreateAsync"/>);
/// the per-tick <see cref="ImportAsync"/> is DML-only and assumes the tables already exist.
///
/// Renting from the pool's READ surface (<see cref="KontextConnectionPool.ExecuteAsync{T}"/>) is
/// deliberate. The writer discipline — a dedicated connection, commit-conflict retry, prepared
/// statement reuse — exists for Lance commits; none of it applies to a plain-table INSERT, and the
/// import scheduler's tick gate already serializes ticks, so renting keeps the importer stateless.
/// </summary>
public sealed class AgentSessionImporter(KontextConnectionPool connections, AgentSessionImportOptions options) {
    // The one-time bootstrap DDL, verbatim from spike-bootstrap.sql: the two tables in the engine
    // catalog's main schema, one command. IF NOT EXISTS makes CreateAsync idempotent; the columns are
    // explicit on purpose — an extension upgrade that adds columns must not silently reshape our tables.
    const string BootstrapSql =
        """
        CREATE TABLE IF NOT EXISTS transcripts (
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

        CREATE TABLE IF NOT EXISTS transcript_parse_errors (
          source       VARCHAR,
          session_id   VARCHAR,
          project_path VARCHAR,
          project_dir  VARCHAR,
          file_name    VARCHAR,
          line_number  BIGINT,
          error        VARCHAR,
          scanned_at   TIMESTAMPTZ
        );
        """;

    // The existence/count probe: duckdb_tables() lists across every attached catalog and never throws,
    // so it answers "does the table exist yet" without touching a table that may not be there. Filtered
    // to the engine catalog's main schema (current_database(), schema 'main') so it targets exactly our
    // unqualified tables and can never match an 'ldb' Lance dataset.
    const string TableExistsSql =
        """
        SELECT count(*)
        FROM duckdb_tables()
        WHERE database_name = current_database()
          AND schema_name = 'main'
          AND table_name = $table_name
        """;

    // Composed ONCE at construction, never per call: the source path cannot be a bound parameter
    // (read_conversations takes it as a SQL literal, the same rule that embeds KontextSchema's
    // dataset path), so it is resolved and quote-escaped here rather than re-escaped every tick.
    readonly string _importSql = BuildImportSql(options.SourcePath);

    /// <summary>
    /// Creates the two transcript tables (<c>transcripts</c> and <c>transcript_parse_errors</c>).
    /// Idempotent — safe to run on every host start (both use IF NOT EXISTS). Mirrors
    /// <see cref="KontextSchema.CreateAsync"/>: the host gets ONE place to bootstrap this storage
    /// before any import tick touches the tables, keeping the per-tick batch DML-only.
    /// </summary>
    public Task CreateAsync(CancellationToken ct = default) =>
        connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = BootstrapSql;
                command.ExecuteNonQuery();
            }, ct);

    /// <summary>
    /// Runs the one idempotent per-tick import batch: load the extension, materialize a single scan,
    /// append the new transcript tail from it, and refresh the parse-error snapshot from the same
    /// scan. DML only — <see cref="CreateAsync"/> owns the table DDL, so this assumes the tables
    /// already exist (the scheduler quiet-skips any tick that fires before bootstrap).
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
    /// behind. The incremental key is <c>uuid</c> (message identity); forked or resumed sessions copy
    /// prefix messages into new files, so the incoming scan is deduped by uuid before the ANTI JOIN
    /// drops everything already stored. The parse-error snapshot is refreshed with <c>DELETE</c> +
    /// <c>INSERT</c> (stable table identity now that DDL is bootstrap-owned) — it is a snapshot, not
    /// a log.
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
    /// Determines whether the transcript tables exist yet — the import scheduler's quiet-skip probe
    /// for ticks that fire before <see cref="CreateAsync"/> has run. Mirrors
    /// <see cref="KontextSchema.ExistsAsync"/>, filtering the engine catalog's <c>main</c> schema
    /// (current_database(), not the Lance 'ldb' alias). Bootstrap creates both tables together, so
    /// probing <c>transcripts</c> answers for both.
    /// </summary>
    public Task<bool> ExistsAsync(CancellationToken ct = default) =>
        connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = TableExistsSql;
                command.Parameters.Add(new("table_name", "transcripts"));

                return (long)command.ExecuteScalar()! > 0;
            }, ct);

    /// <summary>
    /// The total imported transcript count — the probe tests and callers read state through. Tolerant
    /// of a call that lands before <see cref="CreateAsync"/> has created the table: returns 0 instead
    /// of failing.
    /// </summary>
    public Task<long> CountAsync(CancellationToken ct = default) => CountTableAsync("transcripts", ct);

    /// <summary>
    /// The current parse-error snapshot count — the lines this extension build cannot parse yet.
    /// Mirrors <see cref="CountAsync"/>, including the tolerance of a table that does not exist before
    /// bootstrap.
    /// </summary>
    public Task<long> CountParseErrorsAsync(CancellationToken ct = default) => CountTableAsync("transcript_parse_errors", ct);

    Task<long> CountTableAsync(string tableName, CancellationToken ct) =>
        connections.ExecuteAsync(
            connection => {
                // A bare SELECT count(*) FROM <table> binds the table name at PARSE time, so it throws
                // before CreateAsync has bootstrapped the table rather than returning 0. Probe first
                // (the same current_database()/main filter ExistsAsync uses, so it never matches an ldb
                // Lance table), count only when the table is really there — two commands on one rented
                // connection because a single batch would still fail to parse its second statement.
                using var probe = connection.CreateCommand();
                probe.CommandText = TableExistsSql;
                probe.Parameters.Add(new("table_name", tableName));

                if ((long)probe.ExecuteScalar()! == 0)
                    return 0L;

                using var count = connection.CreateCommand();
                // A table name cannot be a bound parameter, but tableName is a hardcoded literal here
                // (never caller input), so embedding it carries no injection surface.
                count.CommandText = $"SELECT count(*) FROM {tableName}";

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

             CREATE OR REPLACE TEMP TABLE agent_scan AS
             SELECT * FROM read_conversations(path := '{escapedPath}', source := 'claude');

             INSERT INTO transcripts BY NAME
             SELECT
               rc.source, rc.session_id, rc.project_path, rc.project_dir, rc.file_name,
               rc.is_agent, rc.line_number, rc.message_type, rc.uuid, rc.parent_uuid,
               try_cast(rc.timestamp AS TIMESTAMPTZ) AS "timestamp",
               rc.message_role, rc.message_content, rc.model, rc.tool_name, rc.tool_use_id,
               rc.tool_input, rc.input_tokens, rc.output_tokens, rc.cache_creation_tokens,
               rc.cache_read_tokens, rc.slug, rc.git_branch, rc.cwd, rc.version,
               rc.stop_reason, rc.repository
             FROM agent_scan AS rc
             ANTI JOIN transcripts AS m ON rc.uuid = m.uuid
             WHERE rc.message_type <> '_parse_error'
               AND rc.uuid IS NOT NULL
             QUALIFY row_number() OVER (PARTITION BY rc.uuid ORDER BY rc.file_name, rc.line_number) = 1;

             DELETE FROM transcript_parse_errors;

             INSERT INTO transcript_parse_errors
             SELECT
               rc.source, rc.session_id, rc.project_path, rc.project_dir, rc.file_name,
               rc.line_number, rc.message_content AS error, now() AS scanned_at
             FROM agent_scan AS rc
             WHERE rc.message_type = '_parse_error';

             DROP TABLE agent_scan;
             """;
    }
}
