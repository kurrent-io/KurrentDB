using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Crud;

/// <summary>
/// Regression coverage for <see cref="DuckDBModelBuilder"/>'s storage-name case normalization
/// (see its <c>Customize</c> XML doc for the full rationale): the DuckDB <c>lance</c> extension's
/// DELETE predicate pushdown silently no-ops on mixed-case column identifiers, so a record deleted
/// through a model with default (PascalCase-derived) storage names would previously appear to
/// succeed while the row survived. This uses a POCO that supplies NO storage-name overrides at all,
/// so every column name on the wire is entirely a product of automatic normalization.
/// </summary>
[LanceRequired]
public class DuckDBCaseNormalizationTests {
    [Test]
    public async Task DefaultNamedPoco_UpsertGetDelete_RoundTrips_AndPhysicalColumnsAreLowercase() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = new(new() { DatabasePath = Path.Combine(dir, "duck.db") });
            var collection = store.GetCollection<string, DocRecord>("docs");

            await collection.EnsureCollectionExistsAsync();

            var record = new DocRecord {
                Id       = "doc1",
                Category = "science",
                Vec      = new([1f, 2f, 3f, 4f])
            };

            await collection.UpsertAsync(record);

            var got = await collection.GetAsync("doc1", new() { IncludeVectors = true });

            await Assert.That(got).IsNotNull();
            await Assert.That(got!.Id).IsEqualTo("doc1");
            await Assert.That(got.Category).IsEqualTo("science");
            await Assert.That(got.Vec.ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f });

            await collection.DeleteAsync("doc1");

            // This is the assertion that catches the silent-delete bug: with mixed-case column names,
            // the lance extension's DELETE predicate pushdown silently matched zero rows, so the record
            // would still be found here even after DeleteAsync "succeeded".
            var afterDelete = await collection.GetAsync("doc1", new() { IncludeVectors = true });
            await Assert.That(afterDelete).IsNull();

            // Verify directly against DuckDB's catalog that the physical column names are lowercase,
            // even though every property on DocRecord uses its default (PascalCase) storage name.
            var columnNames = await store.ConnectionManager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT column_name FROM duckdb_columns() WHERE database_name = ? AND table_name = ?";
                    command.Parameters.Add(new(store.ConnectionManager.StorageAlias));
                    command.Parameters.Add(new(store.Resolver.GetTableName("docs")));

                    var       names   = new List<string>();
                    using var reader  = command.ExecuteReader();
                    var       ordinal = reader.GetOrdinal("column_name");

                    while (reader.Read()) {
                        names.Add(reader.GetString(ordinal));
                    }

                    return names;
                },
                CancellationToken.None);

            await Assert.That(columnNames.Count).IsGreaterThan(0);

            foreach (var columnName in columnNames)
                await Assert.That(columnName).IsEqualTo(columnName.ToLowerInvariant());

            await Assert.That(columnNames).Contains("id");
            await Assert.That(columnNames).Contains("category");
            await Assert.That(columnNames).Contains("vec");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-case-normalization-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void TryDeleteDir(string dir) {
        try {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        } catch (IOException) {
            // Best-effort cleanup.
        } catch (UnauthorizedAccessException) {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// Deliberately supplies NO storage-name overrides: every column name on the wire is produced
    /// entirely by <see cref="DuckDBModelBuilder"/>'s automatic lowercase normalization.
    /// </summary>
    sealed class DocRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string Category { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }
}