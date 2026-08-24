// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

/// <summary>
/// The history as one DuckDB table. It carries no constraints and no qualified name on
/// purpose: uniqueness is the stream's own invariant (validated by the engine), and the
/// executor's connection decides which catalog the history lives in — the same one the
/// migrations build, so both travel together.
/// </summary>
public sealed class DuckLanceSchemaJournal(IDuckLanceSchemaExecutor executor, TimeProvider? timeProvider = null) : IMigrationJournal {
    public const string TableName = "schema_migrations";

    // Tests supply a fake TimeProvider for deterministic timestamps.
    readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask EnsureAsync(CancellationToken ct = default) =>
        executor.ExecuteAsync(static conn => {
            conn.ExecuteAdHocNonQuery(
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version      UINTEGER,
                    key          VARCHAR,
                    executed_at  BIGINT,
                    duration_ms  BIGINT,
                    script       VARCHAR
                )
                """u8);
        }, ct);

    public ValueTask RecordAsync(ExecutedMigration entry, CancellationToken ct = default) {
        var executedAt = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        return executor.ExecuteAsync(conn => {
            conn.ExecuteNonQuery<RecordedMigration, RecordMigration>(new(entry, executedAt));
        }, ct);
    }

    public ValueTask<IReadOnlyList<ExecutedMigration>> ListAsync(CancellationToken ct = default) =>
        executor.ExecuteAsync(static IReadOnlyList<ExecutedMigration> (conn) =>
            conn.ExecuteQuery<ExecutedMigration, MigrationHistory>().ToList(), ct);
}

/// <summary>The journal row plus the clock reading the journal supplies for it.</summary>
readonly record struct RecordedMigration(ExecutedMigration Entry, long ExecutedAt);

file readonly struct MigrationHistory : IQuery<ExecutedMigration> {
    public static ReadOnlySpan<byte> CommandText =>
        "SELECT version, key, script, duration_ms FROM schema_migrations ORDER BY version"u8;

    public static ExecutedMigration Parse(ref DataChunk.Row row) =>
        new(row.ReadUInt32(), row.ReadString(), row.ReadString(), TimeSpan.FromMilliseconds(row.ReadInt64()));
}

file readonly struct RecordMigration : IPreparedStatement<RecordedMigration> {
    public static ReadOnlySpan<byte> CommandText =>
        "INSERT INTO schema_migrations (version, key, executed_at, duration_ms, script) VALUES (?, ?, ?, ?, ?)"u8;

    public static StatementBindingResult Bind(in RecordedMigration recorded, PreparedStatement source) {
        source.Bind(1, recorded.Entry.Version);
        source.Bind(2, recorded.Entry.Key);
        source.Bind(3, recorded.ExecutedAt);
        source.Bind(4, (long)recorded.Entry.Duration.TotalMilliseconds);
        source.Bind(5, recorded.Entry.Script);

        return new(source, completed: true);
    }
}
