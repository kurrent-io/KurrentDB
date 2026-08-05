// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// Seeds the memories read model exactly how the projector will write it. The write path is not
/// built yet, so the integration suites create the schema through <see cref="KontextSchema"/> and
/// insert rows directly with SQL.
/// </summary>
public static class MemorySeeding {
	public static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	/// <summary>Creates the schema and seeds the given rows, then hands back a store over the same pool.</summary>
	public static async ValueTask<KontextDataStore> Seed(KontextConnectionPool pool, int dimension, params MemoryRow[] rows) {
		await CreateSchema(pool, dimension);
		Insert(pool, rows);

		return new(pool);
	}

	/// <summary>
	/// Creates the table and every eager index — the schema component owns all DDL, including the FTS
	/// INVERTED index the keyword leg needs. The dimension must match the vectors the rows carry.
	/// </summary>
	public static ValueTask CreateSchema(KontextConnectionPool pool, int dimension) =>
		new(new KontextSchema(pool, new() { Dimension = dimension }).CreateAsync());

	/// <summary>Inserts rows into an already-created schema; a corpus seeds across several calls.</summary>
	public static void Insert(KontextConnectionPool pool, params MemoryRow[] rows) {
		// Chunked: a 400-row corpus in one statement is ~7,600 parameters.
		const int chunkSize = 64;

		var insertInto = $"INSERT INTO ldb.main.memories (\n  {string.Join(",\n  ", Columns.Select(column => column.Name))})\nVALUES";
		var tuple      = "(" + string.Join(", ", Enumerable.Repeat("?", Columns.Length)) + ")";

		foreach (var chunk in rows.Chunk(chunkSize)) {
			var values = string.Join(",\n", Enumerable.Repeat(tuple, chunk.Length));

			using (pool.Rent(out var connection)) {
				using var insert = connection.CreateCommand();
				insert.CommandText = $"{insertInto}\n{values}";

				foreach (var row in chunk)
				foreach (var (_, value) in Columns)
					insert.Parameters.Add(new DuckDBParameter(value(row) ?? DBNull.Value));

				insert.ExecuteNonQuery();
			}
		}
	}

	// DuckDB.NET parameters, not Kurrent.Quack typed statements: Quack cannot bind the FLOAT[N]
	// embedding or the VARCHAR[] tags. Null binds as NULL; supersedes stays neutral — the tests
	// never read it.
	static readonly (string Name, Func<MemoryRow, object?> Value)[] Columns = [
		("memory_id",        row => row.Id),
		("memory_type",      row => (int)row.Type),
		("content",          row => row.Content),
		("importance",       row => (int)row.Importance),
		("tags",             row => row.Tags),
		("reasoning",        _   => ""),
		("evidence",         row => row.Evidence),
		("supersedes",       _   => new List<string>()),
		("validity_start",   row => row.ValidityStart?.ToUnixTimeMilliseconds()),
		("validity_end",     row => row.ValidityEnd?.ToUnixTimeMilliseconds()),
		("retained_at",      row => row.RetainedAt.ToUnixTimeMilliseconds()),
		("last_accessed_at", row => (row.LastAccessedAt ?? row.RetainedAt).ToUnixTimeMilliseconds()),
		("is_retracted",     row => row.IsRetracted),
		("retracted_at",     row => row.RetractedAt?.ToUnixTimeMilliseconds()),
		("is_superseded",    row => row.IsSuperseded),
		("superseded_at",    row => row.SupersededAt?.ToUnixTimeMilliseconds()),
		("superseded_by",    row => row.SupersededBy),
		("embedding",        row => row.Embedding),
	];
}

/// <summary>One seed row: the fields the tests set, with neutral defaults for the rest.</summary>
public sealed record MemoryRow(
	string                     Id,
	Contracts.MemoryType       Type,
	string                     Content,
	Contracts.MemoryImportance Importance,
	DateTimeOffset             RetainedAt
) {
	/// <summary>
	/// Inert unless the pipeline has a vector leg; the default just keeps the row well-formed and
	/// matches the 4-dim schema the keyword suites create. Suites that rank on vectors set it.
	/// </summary>
	public float[]         Embedding      { get; init; } = [1f, 0f, 0f, 0f];

	public List<string>    Tags           { get; init; } = [];
	public List<string>    Evidence       { get; init; } = [];
	public DateTimeOffset? ValidityStart  { get; init; }
	public DateTimeOffset? ValidityEnd    { get; init; }
	public DateTimeOffset? LastAccessedAt { get; init; }
	public bool            IsRetracted    { get; init; }
	public DateTimeOffset? RetractedAt    { get; init; }
	public bool            IsSuperseded   { get; init; }
	public DateTimeOffset? SupersededAt   { get; init; }
	public string          SupersededBy   { get; init; } = "";
}

/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
public sealed class TempDir : IDisposable {
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-tests", Guid.NewGuid().ToString("N"));

	public TempDir() => Directory.CreateDirectory(Path);

	public void Dispose() {
		try {
			if (Directory.Exists(Path))
				Directory.Delete(Path, recursive: true);
		} catch (IOException) {
			// Best-effort cleanup; a lingering native handle must not fail the test.
		} catch (UnauthorizedAccessException) {
			// Best-effort cleanup.
		}
	}
}
