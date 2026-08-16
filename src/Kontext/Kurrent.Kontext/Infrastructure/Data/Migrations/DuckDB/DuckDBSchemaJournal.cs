// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckDB;

/// <summary>
/// The history as one DuckDB table. It carries no constraints and no qualified name on
/// purpose: uniqueness is the stream's own invariant (validated by the engine), and the
/// executor's connection decides which catalog the history lives in — the same one the
/// steps build, so both travel together.
/// </summary>
public sealed class DuckDBSchemaJournal(IDuckDBSchemaExecutor executor) : IMigrationJournal {
    public ValueTask EnsureAsync(CancellationToken ct = default) =>
        executor.ExecuteAsync(
            static connection => {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                        version      INTEGER,
                        name         VARCHAR,
                        executed_at  BIGINT,
                        duration_ms  BIGINT
                    )
                    """;
                command.ExecuteNonQuery();
            }, ct);

    public ValueTask<int> LoadCurrentVersionAsync(CancellationToken ct = default) =>
        executor.ExecuteAsync(
            static connection => {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT max(version) FROM schema_migrations";

                // max() over an empty table yields NULL, which ADO surfaces as DBNull.
                return command.ExecuteScalar() is { } value and not DBNull
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : 0;
            }, ct);

    public ValueTask RecordAsync(ExecutedMigration entry, CancellationToken ct = default) =>
        executor.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO schema_migrations (version, name, executed_at, duration_ms)
                    VALUES ($version, $name, epoch_ms(now()), $duration_ms)
                    """;
                command.Parameters.Add(new("version", entry.Version));
                command.Parameters.Add(new("name", entry.Name));
                command.Parameters.Add(new("duration_ms", (long)entry.Duration.TotalMilliseconds));
                command.ExecuteNonQuery();
            }, ct);
}
