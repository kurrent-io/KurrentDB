// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckDB;

/// <summary>
/// The DuckDB engine. Its reset drops every table in the active catalog — the migration
/// history falls in the same sweep, which is what makes the replay start from zero. The
/// teardown needs no knowledge of shape; the catalog self-describes.
/// </summary>
public sealed class DuckDBMigrationEngine(MigrationEngineOptions<IDuckDBSchemaExecutor> options)
    : MigrationEngine<IDuckDBSchemaExecutor>(options) {
    protected override Task ResetAsync(IDuckDBSchemaExecutor executor, CancellationToken ct) =>
        executor.ExecuteAsync(
            static connection => {
                var tables = new List<string>();

                using (var query = connection.CreateCommand()) {
                    query.CommandText =
                        """
                        SELECT table_name
                        FROM duckdb_tables()
                        WHERE database_name = current_database()
                        """;

                    using var reader = query.ExecuteReader();

                    while (reader.Read())
                        tables.Add(reader.GetString(0));
                }

                if (tables.Count == 0)
                    return;

                // Not a single statement because DuckDB permits none: DROP TABLE takes one name,
                // and there is no dynamic SQL to consume duckdb_tables() server-side. The list
                // comes back once, and every DROP ships as one batched command.
                var drops = string.Join('\n', tables.Select(static table => $"DROP TABLE IF EXISTS {QuoteIdentifier(table)} CASCADE;"));

                connection.ExecuteAdHocNonQuery(drops, multipleStatements: true);

                static string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
            }, ct).AsTask();
}
