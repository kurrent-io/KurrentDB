using System.Data.Common;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Indexing;

/// <summary>
/// Integration tests for the Stage 7B index wiring in <see cref="DuckDBCollection{TKey,TRecord}.EnsureCollectionExistsAsync"/>
/// and <see cref="DuckDBCollection{TKey, TRecord}.EnsureVectorIndexesAsync"/>: scalar indexes are created eagerly
/// when a collection's table is first created, and vector indexes are created lazily — once the table holds at
/// least 256 rows — either via an explicit <c>EnsureVectorIndexesAsync</c> call or by re-ensuring an existing,
/// populated collection. Each test drives a real DuckDB + <c>lance</c> connection against a temporary storage
/// directory, so the class is gated by <see cref="LanceRequiredAttribute"/>.
/// </summary>
[LanceRequired]
public class DuckDBIndexWiringTests {
    [Test]
    public async Task EnsureCollectionExistsAsync_NewCollection_CreatesScalarIndexesButNoVectorIndex() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, WiringRecord>("docs");

            await collection.EnsureCollectionExistsAsync();

            var observed = await ShowIndexesAsync(store, collection.DatasetPath);

            await Assert.That(observed.Any(i => i.Name == "category_idx"    && IndexTypeMatches(i.Type, "BTREE"))).IsTrue();
            await Assert.That(observed.Any(i => i.Name == "tags_idx"        && IndexTypeMatches(i.Type, "LABEL_LIST"))).IsTrue();
            await Assert.That(observed.Any(i => i.Name == "content_fts_idx" && IndexTypeMatches(i.Type, "INVERTED"))).IsTrue();

            // No vector index yet: the table was just created (empty), and Lance cannot train a vector index
            // against an empty dataset.
            await Assert.That(observed.Any(i => IsOnField(i, "vec"))).IsFalse();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task Search_ThreeSeededRecords_NoVectorIndex_ReturnsOracleDistances() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, WiringRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() {
                    Id       = "k1",
                    Category = "science",
                    Tags     = ["a"],
                    Content  = "hello",
                    Vec      = new([1f, 0f, 0f, 0f])
                },
                new() {
                    Id       = "k2",
                    Category = "science",
                    Tags     = ["a"],
                    Content  = "hello",
                    Vec      = new([0f, 1f, 0f, 0f])
                },
                new() {
                    Id       = "k3",
                    Category = "science",
                    Tags     = ["a"],
                    Content  = "hello",
                    Vec      = new([-1f, 0f, 0f, 0f])
                }
            ]);

            // Confirm the brute-force premise: still no vector index at 3 rows (far below the 256-row floor).
            var observed = await ShowIndexesAsync(store, collection.DatasetPath);
            await Assert.That(observed.Any(i => IsOnField(i, "vec"))).IsFalse();

            var results = await SearchToListAsync(collection.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), 3));

            await Assert.That(results.Select(r => r.Record.Id)).IsEquivalentTo(new[] { "k1", "k2", "k3" });
            await Assert.That(results[0].Record.Id).IsEqualTo("k1");
            await Assert.That(results[1].Record.Id).IsEqualTo("k2");
            await Assert.That(results[2].Record.Id).IsEqualTo("k3");
            await Assert.That(results[0].Score).IsEqualTo(0d);
            await Assert.That(results[1].Score).IsEqualTo(2d);
            await Assert.That(results[2].Score).IsEqualTo(4d);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task EnsureVectorIndexesAsync_FewerThan256Rows_ReturnsZero_AndCreatesNoIndex() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, WiringRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
                new WiringRecord {
                    Id       = "k1",
                    Category = "science",
                    Tags     = ["a"],
                    Content  = "hello",
                    Vec      = new([1f, 0f, 0f, 0f])
                });

            var created = await collection.EnsureVectorIndexesAsync();

            await Assert.That(created).IsEqualTo(0);

            var observed = await ShowIndexesAsync(store, collection.DatasetPath);
            await Assert.That(observed.Any(i => IsOnField(i, "vec"))).IsFalse();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task EnsureVectorIndexesAsync_AtLeast256Rows_CreatesIvfPqIndex_AndIsIdempotent() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, WiringRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await SeedBulkRowsAsync(store, collection.QualifiedTableName, 300);

            var created = await collection.EnsureVectorIndexesAsync();
            await Assert.That(created).IsEqualTo(1);

            var observed = await ShowIndexesAsync(store, collection.DatasetPath);
            await Assert.That(observed.Any(i => i.Name == "vec_idx" && IndexTypeMatches(i.Type, "IVF_PQ"))).IsTrue();

            // Idempotent: an index now exists on "vec", so a second call finds it via SHOW INDEXES and creates
            // nothing further.
            var createdAgain = await collection.EnsureVectorIndexesAsync();
            await Assert.That(createdAgain).IsEqualTo(0);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task EnsureCollectionExistsAsync_ReEnsureOnPopulatedCollection_PicksUpVectorIndex_WithNoDriftThrow() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, WiringRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await SeedBulkRowsAsync(store, collection.QualifiedTableName, 300);

            // The table-just-created path never creates a vector index (the table was empty at that time).
            var beforeReEnsure = await ShowIndexesAsync(store, collection.DatasetPath);
            await Assert.That(beforeReEnsure.Any(i => IsOnField(i, "vec"))).IsFalse();

            // A second collection object for the SAME name and the SAME model shape: this exercises the
            // already-exists (drift-check) path, which must not throw for a matching schema, and must pick up
            // the vector index now that the table has crossed the 256-row floor.
            var collectionB = store.GetCollection<string, WiringRecord>("docs");
            await collectionB.EnsureCollectionExistsAsync();

            var afterReEnsure = await ShowIndexesAsync(store, collection.DatasetPath);
            await Assert.That(afterReEnsure.Any(i => i.Name == "vec_idx" && IndexTypeMatches(i.Type, "IVF_PQ"))).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task EnsureVectorIndexesAsync_FlatIndexKind_NeverCreatesAnIndex_EvenWithEnoughRows() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FlatVectorRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await SeedBulkRowsAsync(store, collection.QualifiedTableName, 300);

            var created = await collection.EnsureVectorIndexesAsync();

            await Assert.That(created).IsEqualTo(0);

            var observed = await ShowIndexesAsync(store, collection.DatasetPath);
            await Assert.That(observed.Any(i => IsOnField(i, "vec"))).IsFalse();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    /// <summary>
    /// Bulk-seeds <paramref name="rowCount"/> filler rows directly via SQL, bypassing the ORM's per-row
    /// <c>MERGE</c> in <c>UpsertAsync</c> for speed. Column order/types match <see cref="WiringRecord"/>'s and
    /// <see cref="FlatVectorRecord"/>'s shared "docs" table shape exactly: <c>id VARCHAR, category VARCHAR,
    /// tags VARCHAR[], content VARCHAR, vec FLOAT[4]</c>.
    /// </summary>
    static async Task SeedBulkRowsAsync(DuckDBVectorStore store, string qualifiedTableName, int rowCount) =>
        await store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();

                command.CommandText =
                    $"INSERT INTO {qualifiedTableName} "
                  + "SELECT 'r' || i, 'cat', ['t'], 'filler ' || i, "
                  + "[0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8]::FLOAT[4] "
                  + $"FROM range({rowCount}) t(i)";

                command.ExecuteNonQuery();
            },
            CancellationToken.None);

    static Task<IReadOnlyList<(string Name, string Type, string Fields, long RowsIndexed)>> ShowIndexesAsync(DuckDBVectorStore store, string datasetPath) =>
        store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SHOW INDEXES ON '{EscapePath(datasetPath)}'";
                using var reader = command.ExecuteReader();
                return DuckDBIndexBuilder.ParseShowIndexes(reader);
            },
            CancellationToken.None);

    /// <summary>Whether an observed <c>SHOW INDEXES</c> row indexes the given column, ignoring case.</summary>
    static bool IsOnField((string Name, string Type, string Fields, long RowsIndexed) index, string field) =>
        string.Equals(index.Fields, field, StringComparison.OrdinalIgnoreCase);

    /// <summary>See <see cref="DuckDBIndexingIntegrationTests.IndexTypeMatches"/> for why this normalizes both case and underscores.</summary>
    static bool IndexTypeMatches(string observedType, string usingType) =>
        string.Equals(
            observedType.Replace("_", "", StringComparison.Ordinal),
            usingType.Replace("_", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

    static string EscapePath(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    static async Task<List<VectorSearchResult<T>>> SearchToListAsync<T>(IAsyncEnumerable<VectorSearchResult<T>> source)
        where T : class {
        var results = new List<VectorSearchResult<T>>();

        await foreach (var result in source)
            results.Add(result);

        return results;
    }

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-index-wiring-test-" + Guid.NewGuid().ToString("N"));
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

    // Storage (column) names are deliberately lowercase; see the note in DuckDBCollectionCrudTests about the
    // lance extension's broken DELETE predicate pushdown for uppercase column names. DistanceFunction is
    // pinned to EuclideanSquaredDistance so the brute-force search test has a simple 0/2/4 oracle; IndexKind is
    // deliberately left at its default (null -> IVF_PQ) so the vector-index-wiring tests exercise the real
    // provider default.
    sealed class WiringRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags", IsIndexed = true)]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content", IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.EuclideanSquaredDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    /// <summary>Same shape as <see cref="WiringRecord"/>, but with <see cref="IndexKind.Flat"/> pinned on the vector, meaning "never build an index" rather than "not yet built".</summary>
    sealed class FlatVectorRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags", IsIndexed = true)]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content", IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec", IndexKind = IndexKind.Flat)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}