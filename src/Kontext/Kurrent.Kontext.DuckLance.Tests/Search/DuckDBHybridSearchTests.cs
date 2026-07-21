using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Search;

/// <summary>
/// Integration tests for <see cref="DuckDBCollection{TKey,TRecord}.HybridSearchAsync{TInput}"/> (the
/// <see cref="IKeywordHybridSearchable{TRecord}"/> surface). Each test drives a real DuckDB + <c>lance</c>
/// connection against a temporary storage directory, blending dense vector search with full-text keyword search
/// over an <c>IsFullTextIndexed</c> column, so the class is gated by <see cref="LanceRequiredAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnsureCollectionExistsAsync</c> auto-creates the <c>INVERTED</c> full-text index for every
/// <c>IsFullTextIndexed</c> data property, so these tests rely on it rather than creating the index by hand.
/// Test POCOs use lowercase storage-name overrides for every column, matching the golden convention and
/// side-stepping the <c>lance</c> extension's uppercase-column pushdown bug.
/// </para>
/// <para>
/// The blend BDD test also closes the last open micro-gap from the spike: it exercises the keyword query text
/// bound as a positional (<c>?</c>) parameter end to end (the spike only ever bound the query vector as a
/// parameter and inlined the text as a literal).
/// </para>
/// </remarks>
[LanceRequired]
public class DuckDBHybridSearchTests {
    static ReadOnlyMemory<float> Query() => new([1f, 0f, 0f, 0f]);

    // 1. BDD blend — vector and keyword agree, and this closes the text-as-parameter micro-gap.
    // A "the quick brown fox jumps" [1,0,0,0]; B "the lazy dog sleeps" [0,1,0,0]; C "quack goes the duck" [-1,0,0,0].
    // Querying vector [1,0,0,0] + keyword "fox": A wins both modalities, so it must rank first; every result carries
    // a non-null score, and the scores are non-increasing (ORDER BY _hybrid_score DESC).
    [Test]
    public async Task HybridSearch_VectorAndKeywordAgree_RanksAgreedDocumentFirst() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, HybridRecord>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedBlendCorpusAsync(collection);

            var results = await SearchToListAsync(collection.HybridSearchAsync(Query(), ["fox"], 3));

            await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(results[0].Record.Id).IsEqualTo("a");
            await Assert.That(results.All(r => r.Score is not null)).IsTrue();
            await Assert.That(IsNonIncreasing(results)).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 2. Keyword-vs-vector disagreement — vector points at B, keyword "fox" matches A. The blend must surface BOTH
    // (C is far on both modalities), so A and B are the top 2. Order between them is blend-dependent, so is not
    // asserted.
    [Test]
    public async Task HybridSearch_VectorAndKeywordDisagree_SurfacesBothInTopResults() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, HybridRecord>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedBlendCorpusAsync(collection);

            // Query vector equals B's vector; keyword still matches A.
            var results = await SearchToListAsync(collection.HybridSearchAsync(new ReadOnlyMemory<float>([0f, 1f, 0f, 0f]), ["fox"], 3));

            var topTwo = results.Take(2).Select(r => r.Record.Id).ToList();
            await Assert.That(topTwo).Contains("a");
            await Assert.That(topTwo).Contains("b");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 3. Multiple keywords are joined into one full-text query ("fox jumps"); A contains both and still wins.
    [Test]
    public async Task HybridSearch_MultipleKeywords_JoinsAndStillFindsMatch() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, HybridRecord>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedBlendCorpusAsync(collection);

            var results = await SearchToListAsync(collection.HybridSearchAsync(Query(), ["fox", "jumps"], 3));

            await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(results[0].Record.Id).IsEqualTo("a");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 4. Filtered hybrid, equality — a category equality WHERE narrows the search to the matching category only,
    // pushed down as a TRUE prefilter through hybrid.
    [Test]
    public async Task HybridSearch_EqualityFilter_NarrowsToMatchingCategory() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, HybridRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                Doc(
                    "a1", "cat_a", ["t"],
                    "fox runs fast", [1f, 0f, 0f, 0f]),
                Doc(
                    "a2", "cat_a", ["t"],
                    "the dog barks", [0f, 1f, 0f, 0f]),
                Doc(
                    "b1", "cat_b", ["t"],
                    "fox jumps high", [1f, 0f, 0f, 0f]),
                Doc(
                    "b2", "cat_b", ["t"],
                    "a duck swims", [-1f, 0f, 0f, 0f])
            ]);

            var results = await SearchToListAsync(
                collection.HybridSearchAsync(
                    Query(), ["fox"], 10,
                    new() { Filter = r => r.Category == "cat_a" }));

            await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(results.All(r => r.Record.Category == "cat_a")).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 5. Filtered hybrid, containment — a tag-containment WHERE is post-filtered, so the provider oversamples k to
    // the whole table; every tagged row survives regardless of its rank in either modality.
    [Test]
    public async Task HybridSearch_ContainmentFilter_OversamplesAndReturnsTaggedRows() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, HybridRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                Doc(
                    "t1", "cat_a", ["special"],
                    "fox one", [1f, 0f, 0f, 0f]),
                Doc(
                    "t2", "cat_a", ["ordinary"],
                    "fox two", [1f, 0f, 0f, 0f]),
                Doc(
                    "t3", "cat_b", ["special"],
                    "dog three", [0f, 1f, 0f, 0f])
            ]);

            var results = await SearchToListAsync(
                collection.HybridSearchAsync(
                    Query(), ["fox"], 10,
                    new() { Filter = r => r.Tags.Contains("special") }));

            await Assert.That(results.All(r => r.Record.Tags.Contains("special"))).IsTrue();

            await Assert
                .That(results.Select(r => r.Record.Id).OrderBy(keySelector: id => id, StringComparer.Ordinal))
                .IsEquivalentTo(new[] { "t1", "t3" });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 6. Two IsFullTextIndexed properties — the abstraction cannot auto-select, so an unqualified hybrid search
    // throws; selecting the text property via AdditionalProperty resolves it and the search works.
    [Test]
    public async Task HybridSearch_TwoFullTextProperties_RequireAdditionalPropertySelection() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, TwoFullTextRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() {
                    Id      = "d1",
                    Title   = "alpha",
                    Content = "fox",
                    Vec     = new([1f, 0f, 0f, 0f])
                },
                new() {
                    Id      = "d2",
                    Title   = "beta",
                    Content = "dog",
                    Vec     = new([0f, 1f, 0f, 0f])
                }
            ]);

            // Without AdditionalProperty the model has two IsFullTextIndexed properties and cannot pick one.
            await Assert
                .That(async () => await SearchToListAsync(collection.HybridSearchAsync(Query(), ["fox"], 2)))
                .Throws<Exception>();

            // Selecting the content property resolves the ambiguity and the search runs.
            var results = await SearchToListAsync(
                collection.HybridSearchAsync(
                    Query(), ["fox"], 2,
                    new() { AdditionalProperty = r => r.Content }));

            await Assert.That(results.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(results[0].Record.Id).IsEqualTo("d1");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 7. TInput as ReadOnlyMemory<float> and float[] both drive hybrid search identically (the raw-text / generated
    // embedding input path is intentionally not exercised here — the ONNX embedding fixture is heavyweight).
    [Test]
    public async Task HybridSearch_AcceptsReadOnlyMemoryAndFloatArrayInputs() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, HybridRecord>("docs");
            await collection.EnsureCollectionExistsAsync();
            await SeedBlendCorpusAsync(collection);

            var fromMemory = await SearchToListAsync(collection.HybridSearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), ["fox"], 3));
            await Assert.That(fromMemory.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(fromMemory[0].Record.Id).IsEqualTo("a");

            var fromArray = await SearchToListAsync(collection.HybridSearchAsync(new[] { 1f, 0f, 0f, 0f }, ["fox"], 3));
            await Assert.That(fromArray.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(fromArray[0].Record.Id).IsEqualTo("a");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static Task SeedBlendCorpusAsync(DuckDBCollection<string, HybridRecord> collection) =>
        collection.UpsertAsync(
        [
            Doc(
                "a", "cat_a", ["ta"],
                "the quick brown fox jumps", [1f, 0f, 0f, 0f]),
            Doc(
                "b", "cat_b", ["tb"],
                "the lazy dog sleeps", [0f, 1f, 0f, 0f]),
            Doc(
                "c", "cat_c", ["tc"],
                "quack goes the duck", [-1f, 0f, 0f, 0f])
        ]);

    static HybridRecord Doc(string id, string category, List<string> tags, string content, float[] vec) =>
        new() {
            Id       = id,
            Category = category,
            Tags     = tags,
            Content  = content,
            Vec      = new(vec)
        };

    static bool IsNonIncreasing<T>(IReadOnlyList<VectorSearchResult<T>> results)
        where T : class {
        for (var i = 1; i < results.Count; i++) {
            if (results[i - 1].Score < results[i].Score)
                return false;
        }

        return true;
    }

    static async Task<List<VectorSearchResult<T>>> SearchToListAsync<T>(IAsyncEnumerable<VectorSearchResult<T>> source)
        where T : class {
        var results = new List<VectorSearchResult<T>>();

        await foreach (var result in source)
            results.Add(result);

        return results;
    }

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-hybrid-test-" + Guid.NewGuid().ToString("N"));
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

    sealed class HybridRecord {
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

    /// <summary>A record with two IsFullTextIndexed data properties, to exercise the AdditionalProperty selection path.</summary>
    sealed class TwoFullTextRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "title", IsFullTextIndexed = true)]
        public string Title { get; set; } = "";

        [VectorStoreData(StorageName = "content", IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.EuclideanSquaredDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}