using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Search;

/// <summary>
/// Integration tests for <see cref="DuckDBCollection{TKey, TRecord}.SearchAsync{TInput}"/>. Each test drives a
/// real DuckDB + <c>lance</c> connection against a temporary storage directory, so the class is gated by
/// <see cref="LanceRequiredAttribute"/>. The oracle scores are the live-validated Decision #1 numbers.
/// </summary>
/// <remarks>
/// Test POCOs use lowercase storage-name overrides for every column, matching the golden convention and
/// side-stepping the <c>lance</c> extension's uppercase-column pushdown bug.
/// </remarks>
[LanceRequired]
public class DuckDBVectorSearchTests {
    static readonly (string Id, float[] Vec)[] s_knownVectors = [("k1", [1f, 0f, 0f, 0f]), ("k2", [0f, 1f, 0f, 0f]), ("k3", [-1f, 0f, 0f, 0f])];

    // The canonical query and the three known seed vectors: k1 identical, k2 orthogonal, k3 opposite.
    static ReadOnlyMemory<float> Query() => new([1f, 0f, 0f, 0f]);

    // Oracle 1: squared L2 with no index -> raw _distance is squared L2 (0 / 2 / 4).
    [Test]
    public async Task Search_SquaredL2_NoIndex_ReturnsOracleDistances() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SquaredL2Record>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedKnownAsync(collection, factory: (id, vec) => new() { Id = id, Vec = vec });

            var results = await SearchToListAsync(collection.SearchAsync(Query(), 3));

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

    // Oracle 2: cosine distance with a cosine IVF_FLAT index -> _distance = 1 - cosine similarity (0 / 1 / 2).
    [Test]
    public async Task Search_CosineDistance_WithCosineIndex_ReturnsOracleDistances() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, CosineRecord>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedKnownAsync(collection, factory: (id, vec) => new() { Id = id, Vec = vec });
            await CreateCosineIvfFlatIndexAsync(store, collection.DatasetPath, "vec");

            var results = await SearchToListAsync(collection.SearchAsync(Query(), 3));

            await Assert.That(results[0].Record.Id).IsEqualTo("k1");
            await Assert.That(results[1].Record.Id).IsEqualTo("k2");
            await Assert.That(results[2].Record.Id).IsEqualTo("k3");
            await Assert.That(results[0].Score).IsEqualTo(0d);
            await Assert.That(results[1].Score).IsEqualTo(1d);
            await Assert.That(results[2].Score).IsEqualTo(2d);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // Oracle 3: cosine similarity with the same cosine index -> score = cosine similarity (1 / 0 / -1),
    // ordered by ascending _distance (k1, k2, k3).
    [Test]
    public async Task Search_CosineSimilarity_WithCosineIndex_ReturnsOracleScores() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, CosineSimilarityRecord>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedKnownAsync(collection, factory: (id, vec) => new() { Id = id, Vec = vec });
            await CreateCosineIvfFlatIndexAsync(store, collection.DatasetPath, "vec");

            var results = await SearchToListAsync(collection.SearchAsync(Query(), 3));

            await Assert.That(results[0].Record.Id).IsEqualTo("k1");
            await Assert.That(results[1].Record.Id).IsEqualTo("k2");
            await Assert.That(results[2].Record.Id).IsEqualTo("k3");
            await Assert.That(results[0].Score).IsEqualTo(1d);
            await Assert.That(results[1].Score).IsEqualTo(0d);
            await Assert.That(results[2].Score).IsEqualTo(-1d);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // Paging: 25 distinct vectors, two disjoint pages, correctly ordered across the page boundary.
    [Test]
    public async Task Search_Paging_ReturnsDisjointOrderedPages() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SquaredL2Record>("docs");
            await collection.EnsureCollectionExistsAsync();

            var records = new List<SquaredL2Record>(25);

            for (var i = 1; i <= 25; i++)
                // Distinct vectors along the first axis: squared-L2 distance from Query() is (i-1)^2, strictly increasing.
                records.Add(new() { Id = $"k{i:D2}", Vec = new([i, 0f, 0f, 0f]) });

            await collection.UpsertAsync(records);

            var page1 = await SearchToListAsync(collection.SearchAsync(Query(), 10));
            var page2 = await SearchToListAsync(collection.SearchAsync(Query(), 10, new() { Skip = 10 }));

            await Assert.That(page1.Count).IsEqualTo(10);
            await Assert.That(page2.Count).IsEqualTo(10);

            var page1Ids = page1.Select(r => r.Record.Id).ToList();
            var page2Ids = page2.Select(r => r.Record.Id).ToList();

            // Disjoint pages.
            await Assert.That(page1Ids.Intersect(page2Ids, StringComparer.Ordinal).Any()).IsFalse();

            // Each page is ascending, and page 1's last distance is no greater than page 2's first.
            await Assert.That(IsAscending(page1)).IsTrue();
            await Assert.That(IsAscending(page2)).IsTrue();
            await Assert.That(page1[^1].Score <= page2[0].Score).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // Empty collection: table exists, zero rows -> empty result, no exception.
    [Test]
    public async Task Search_EmptyCollection_ReturnsNoResults() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SquaredL2Record>("docs");
            await collection.EnsureCollectionExistsAsync();

            var results = await SearchToListAsync(collection.SearchAsync(Query(), 3));

            await Assert.That(results.Count).IsEqualTo(0);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // IncludeVectors: true returns the stored vector; the default (false) leaves it at its default value.
    [Test]
    public async Task Search_IncludeVectors_ControlsVectorHydration() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SquaredL2Record>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedKnownAsync(collection, factory: (id, vec) => new() { Id = id, Vec = vec });

            var withVectors = await SearchToListAsync(collection.SearchAsync(Query(), 1, new() { IncludeVectors = true }));

            await Assert.That(withVectors[0].Record.Id).IsEqualTo("k1");
            await Assert.That(withVectors[0].Record.Vec.ToArray()).IsEquivalentTo(new[] { 1f, 0f, 0f, 0f });

            var withoutVectors = await SearchToListAsync(collection.SearchAsync(Query(), 1));

            await Assert.That(withoutVectors[0].Record.Id).IsEqualTo("k1");
            await Assert.That(withoutVectors[0].Record.Vec.Length).IsEqualTo(0);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // Multi-vector model: an unqualified search is ambiguous and throws; selecting a vector property works.
    [Test]
    public async Task Search_MultiVector_RequiresVectorPropertySelection() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, MultiVecRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() {
                    Id   = "m1",
                    Vec1 = new([1f, 0f, 0f, 0f]),
                    Vec2 = new([1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f])
                },
                new() {
                    Id   = "m2",
                    Vec1 = new([0f, 1f, 0f, 0f]),
                    Vec2 = new([0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f])
                }
            ]);

            // No VectorProperty on a multi-vector model -> ambiguous -> throws from GetVectorPropertyOrSingle.
            await Assert
                .That(async () => await SearchToListAsync(collection.SearchAsync(Query(), 2)))
                .Throws<Exception>();

            // Selecting a vector property resolves the ambiguity.
            var results = await SearchToListAsync(collection.SearchAsync(Query(), 2, new() { VectorProperty = r => r.Vec1 }));

            await Assert.That(results.Count).IsEqualTo(2);
            await Assert.That(results[0].Record.Id).IsEqualTo("m1");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // An unsupported Filter throws NotSupportedException. (ScoreThreshold is now supported — it is applied
    // client-side after score conversion; see DuckDBHardeningTests for its coverage — so it is no longer asserted
    // to throw here.)
    [Test]
    public async Task Search_UnsupportedOptions_Throw() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SquaredL2Record>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedKnownAsync(collection, factory: (id, vec) => new() { Id = id, Vec = vec });

            await Assert
                .That(async () => await SearchToListAsync(collection.SearchAsync(Query(), 3, new() { Filter = r => r.Category == "science" })))
                .Throws<NotSupportedException>();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // TInput as float[] and Embedding<float> behave like ReadOnlyMemory<float>.
    [Test]
    public async Task Search_AcceptsFloatArrayAndEmbeddingInputs() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SquaredL2Record>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedKnownAsync(collection, factory: (id, vec) => new() { Id = id, Vec = vec });

            var fromArray = await SearchToListAsync(collection.SearchAsync(new[] { 1f, 0f, 0f, 0f }, 3));

            await Assert.That(fromArray[0].Record.Id).IsEqualTo("k1");
            await Assert.That(fromArray[0].Score).IsEqualTo(0d);
            await Assert.That(fromArray.Count).IsEqualTo(3);

            var fromEmbedding = await SearchToListAsync(collection.SearchAsync(new Embedding<float>(new([1f, 0f, 0f, 0f])), 3));

            await Assert.That(fromEmbedding[0].Record.Id).IsEqualTo("k1");
            await Assert.That(fromEmbedding[0].Score).IsEqualTo(0d);
            await Assert.That(fromEmbedding.Count).IsEqualTo(3);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static bool IsAscending<T>(IReadOnlyList<VectorSearchResult<T>> results)
        where T : class {
        for (var i = 1; i < results.Count; i++) {
            if (results[i - 1].Score > results[i].Score)
                return false;
        }

        return true;
    }

    static async Task SeedKnownAsync<T>(DuckDBCollection<string, T> collection, Func<string, ReadOnlyMemory<float>, T> factory)
        where T : class {
        var records = new List<T>(s_knownVectors.Length);

        foreach (var (id, vec) in s_knownVectors)
            records.Add(factory(id, new(vec)));

        await collection.UpsertAsync(records);
    }

    static Task CreateCosineIvfFlatIndexAsync(DuckDBVectorStore store, string datasetPath, string vectorColumn) =>
        store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using var command = connection.CreateCommand();

                command.CommandText =
                    $"CREATE INDEX vec_idx ON '{datasetPath}' ({vectorColumn}) USING IVF_FLAT WITH (metric_type = 'cosine', num_partitions = 1)";

                command.ExecuteNonQuery();
            },
            CancellationToken.None);

    static async Task<List<VectorSearchResult<T>>> SearchToListAsync<T>(IAsyncEnumerable<VectorSearchResult<T>> source)
        where T : class {
        var results = new List<VectorSearchResult<T>>();

        await foreach (var result in source)
            results.Add(result);

        return results;
    }

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-search-test-" + Guid.NewGuid().ToString("N"));
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

    sealed class SquaredL2Record {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = "science";

        [VectorStoreData(StorageName = "tags")]
        public List<string> Tags { get; set; } = ["t"];

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "c";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.EuclideanSquaredDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class CosineRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = "science";

        [VectorStoreData(StorageName = "tags")]
        public List<string> Tags { get; set; } = ["t"];

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "c";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.CosineDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class CosineSimilarityRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = "science";

        [VectorStoreData(StorageName = "tags")]
        public List<string> Tags { get; set; } = ["t"];

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "c";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class MultiVecRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = "science";

        [VectorStoreVector(4, StorageName = "vec1")]
        public ReadOnlyMemory<float> Vec1 { get; set; }

        [VectorStoreVector(8, StorageName = "vec2")]
        public ReadOnlyMemory<float> Vec2 { get; set; }
    }
}