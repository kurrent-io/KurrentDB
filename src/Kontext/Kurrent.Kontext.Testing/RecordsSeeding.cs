// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Records.Data;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// Seeds the records read model the way <c>KontextRecordsWriter</c> writes it, without standing up a
/// consumer: the suites create the schema through <see cref="KontextMigrations"/> and insert rows
/// directly with SQL.
/// </summary>
public static class RecordsSeeding {
    /// <summary>Creates the schema and seeds the given rows, then hands back a store over the same data sources.</summary>
    public static async ValueTask<KontextRecordsStore> Seed(KontextDataSource dataSource, params RecordRow[] rows) {
        await MemorySeeding.CreateSchema(dataSource);
        Insert(dataSource, rows);

        return new(dataSource);
    }

    /// <summary>Inserts rows into an already-created schema; a corpus seeds across several calls.</summary>
    public static void Insert(KontextDataSource dataSource, params RecordRow[] rows) {
        // Chunked for the same reason MemorySeeding chunks: a wide corpus in one statement runs to
        // thousands of parameters.
        const int chunkSize = 64;

        var insertInto = $"INSERT INTO ldb.main.records (\n  {string.Join(",\n  ", Columns.Select(column => column.Name))})\nVALUES";
        var tuple      = "(" + string.Join(", ", Enumerable.Repeat("?", Columns.Length)) + ")";

        using var connection = dataSource.OpenLanceWriter();

        foreach (var chunk in rows.Chunk(chunkSize)) {
            var values = string.Join(",\n", Enumerable.Repeat(tuple, chunk.Length));

            using var insert = connection.CreateCommand();
            insert.CommandText = $"{insertInto}\n{values}";

            foreach (var row in chunk)
            foreach (var (_, value) in Columns)
                insert.Parameters.Add(new DuckDBParameter(value(row) ?? DBNull.Value));

            insert.ExecuteNonQuery();
        }
    }

    // DuckDB.NET parameters, not Kurrent.Quack typed statements: Quack cannot bind the FLOAT[N]
    // embedding.
    static readonly (string Name, Func<RecordRow, object?> Value)[] Columns = [
        ("log_position",  row => row.LogPosition),
        ("record_id",     row => row.RecordId.ToByteArray()),
        ("stream",        row => row.Stream),
        ("category",      row => row.Category),
        ("schema_name",   row => row.SchemaName),
        ("schema_format", row => row.SchemaFormat),
        ("schema_id",     row => row.SchemaId),
        ("data",          row => row.Data ?? row.Content),
        ("created_at",    row => row.CreatedAt.ToUnixTimeMilliseconds()),
        ("content",       row => row.Content),
        ("properties",    row => row.Properties),
        ("embedding",     row => row.Embedding),
    ];
}

/// <summary>One seed row: the fields the tests set, with neutral defaults for the rest.</summary>
public sealed record RecordRow(
    long   LogPosition,
    Guid   RecordId,
    string Stream,
    string Category,
    string SchemaName,
    string Content
) {
    public string  SchemaFormat { get; init; } = "Json";
    public string  SchemaId     { get; init; } = "";

    /// <summary>The properties JSON object, exactly as the indexer writes it.</summary>
    public string  Properties   { get; init; } = "{}";

    /// <summary>Defaults to <see cref="Content"/> — the indexer writes the raw payload here and its
    /// flattened projection to `content`, and most suites do not care that they differ.</summary>
    public string? Data { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Inert unless the query has a vector leg; the default keeps the row well-formed at the
    /// schema's width.</summary>
    public float[] Embedding { get; init; } = MemorySeeding.Vector(1f);
}
