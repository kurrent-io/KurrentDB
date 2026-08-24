// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckLance;

/// <summary>
/// The DuckDB engine. Its reset drops every table in the active catalog — the migration
/// history falls in the same sweep, which is what makes the replay start from zero. The
/// teardown needs no knowledge of shape; the catalog self-describes.
/// </summary>
public class DuckLanceMigrationEngine(DuckLanceMigrationEngineOptions options)
    : MigrationEngine<IDuckLanceSchemaExecutor>(options) {
    protected override ValueTask ResetAsync(IDuckLanceSchemaExecutor ctx, CancellationToken ct) =>
        ctx.ExecuteAsync(static connection => {
            // Scoped to current_database() rather than Quack's GetTables(): the connection has the
            // Lance catalog attached, and an unscoped sweep would drop datasets this engine does
            // not own.
            var tables = connection.ExecuteQuery<string, LocalTables>().ToList();

            if (tables.Count == 0)
                return;

            // Not a single statement because DuckDB permits none: DROP TABLE takes one name,
            // and there is no dynamic SQL to consume duckdb_tables() server-side. The list
            // comes back once, and every DROP ships as one batched command.
            var drops = string.Join('\n', tables.Select(static table => $"DROP TABLE IF EXISTS {QuoteIdentifier(table)} CASCADE;"));

            connection.ExecuteAdHocNonQuery(drops, multipleStatements: true);

            static string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
        }, ct);
}

file readonly struct LocalTables : IQuery<string> {
    public static ReadOnlySpan<byte> CommandText =>
        "SELECT table_name FROM duckdb_tables() WHERE database_name = current_database()"u8;

    public static string Parse(ref DataChunk.Row row) => row.ReadString();
}

public class DuckLanceMigrationEngineOptions : MigrationEngineOptions<IDuckLanceSchemaExecutor> {
    public DuckLanceMigrationEngineOptions() {
        Journal ??= new DuckLanceSchemaJournal(Context!);
    }
}
