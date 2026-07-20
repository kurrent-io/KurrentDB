using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Lifecycle;

/// <summary>
/// Integration tests for <see cref="DuckDBCollection{TKey,TRecord}"/>'s collection-lifecycle
/// operations: <c>CollectionExistsAsync</c>, <c>EnsureCollectionExistsAsync</c>, and
/// <c>EnsureCollectionDeletedAsync</c>. Gated by <see cref="LanceRequiredAttribute"/> because every
/// test drives a real DuckDB + <c>lance</c> connection against a temporary storage directory.
/// </summary>
[LanceRequired]
public class DuckDBCollectionLifecycleTests {
    [Test]
    public async Task EnsureCollectionExistsAsync_CreatesCollection_WhenAbsent() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = new(new() { DatabasePath = Path.Combine(dir, "duck.db") });
            var collection = store.GetCollection<string, DocRecord>("docs");

            await Assert.That(await collection.CollectionExistsAsync()).IsFalse();

            await collection.EnsureCollectionExistsAsync();

            await Assert.That(await collection.CollectionExistsAsync()).IsTrue();

            var datasetDir = Path.Combine(dir, "docs.lance");
            await Assert.That(Directory.Exists(datasetDir)).IsTrue();

            var names = new List<string>();

            await foreach (var name in store.ListCollectionNamesAsync())
                names.Add(name);

            await Assert.That(names.Count).IsEqualTo(1);
            await Assert.That(names[0]).IsEqualTo("docs");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task EnsureCollectionExistsAsync_CalledTwice_IsIdempotent() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = new(new() { DatabasePath = Path.Combine(dir, "duck.db") });
            var collection = store.GetCollection<string, DocRecord>("docs");

            await collection.EnsureCollectionExistsAsync();
            await collection.EnsureCollectionExistsAsync();

            await Assert.That(await collection.CollectionExistsAsync()).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task EnsureCollectionExistsAsync_DifferentSchema_ThrowsVectorStoreException() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

            var collectionA = store.GetCollection<string, DocRecord>("docs");
            await collectionA.EnsureCollectionExistsAsync();

            // A second collection object for the SAME name, built from a different record shape.
            var collectionB = store.GetCollection<string, DocRecordWithExtraProperty>("docs");

            await Assert
                .That(async () => await collectionB.EnsureCollectionExistsAsync())
                .Throws<VectorStoreException>()
                .WithMessageContaining("different schema");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task EnsureCollectionDeletedAsync_RemovesCollection_AndIsIdempotent() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = new(new() { DatabasePath = Path.Combine(dir, "duck.db") });
            var collection = store.GetCollection<string, DocRecord>("docs");

            await collection.EnsureCollectionExistsAsync();

            var datasetDir = Path.Combine(dir, "docs.lance");
            await Assert.That(Directory.Exists(datasetDir)).IsTrue();

            await collection.EnsureCollectionDeletedAsync();

            await Assert.That(await collection.CollectionExistsAsync()).IsFalse();
            await Assert.That(Directory.Exists(datasetDir)).IsFalse();

            // Deleting an already-absent collection must be a no-op.
            await collection.EnsureCollectionDeletedAsync();

            // Deleting a collection that was never created must also be a no-op.
            var neverCreated = store.GetCollection<string, DocRecord>("never_created");
            await neverCreated.EnsureCollectionDeletedAsync();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    /// <summary>
    /// Two stores over separate database directories don't collide.
    /// </summary>
    [Test]
    public async Task TwoStoresOverSeparateDatabaseDirectories_DontCollide() {
        var                dir    = CreateTempStorageDir();
        DuckDBVectorStore? storeA = null;
        DuckDBVectorStore? storeB = null;

        try {
            storeA = new(new() { DatabasePath = Path.Combine(dir, "tenant_a", "duck.db") });
            storeB = new(new() { DatabasePath = Path.Combine(dir, "tenant_b", "duck.db") });

            var collectionA = storeA.GetCollection<string, DocRecord>("docs");
            var collectionB = storeB.GetCollection<string, DocRecord>("docs");

            await collectionA.EnsureCollectionExistsAsync();
            await collectionB.EnsureCollectionExistsAsync();

            var namesA = new List<string>();

            await foreach (var name in storeA.ListCollectionNamesAsync())
                namesA.Add(name);

            var namesB = new List<string>();

            await foreach (var name in storeB.ListCollectionNamesAsync())
                namesB.Add(name);

            await Assert.That(namesA.Count).IsEqualTo(1);
            await Assert.That(namesA[0]).IsEqualTo("docs");
            await Assert.That(namesB.Count).IsEqualTo(1);
            await Assert.That(namesB[0]).IsEqualTo("docs");

            // tenant_a deletes its own "docs" via the store-level proxy; tenant_b's "docs" must survive.
            await storeA.EnsureCollectionDeletedAsync("docs");

            await Assert.That(await storeB.CollectionExistsAsync("docs")).IsTrue();
        } finally {
            storeA?.Dispose();
            storeB?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task Store_CollectionExistsAsync_ReflectsCollectionLifecycle() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

            await Assert.That(await store.CollectionExistsAsync("docs")).IsFalse();

            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await Assert.That(await store.CollectionExistsAsync("docs")).IsTrue();

            await store.EnsureCollectionDeletedAsync("docs");

            await Assert.That(await store.CollectionExistsAsync("docs")).IsFalse();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-collection-lifecycle-test-" + Guid.NewGuid().ToString("N"));
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

    sealed class DocRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(IsIndexed = true)] public string Category { get; set; } = "";

        [VectorStoreData(IsIndexed = true)] public List<string> Tags { get; set; } = [];

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }

    /// <summary>Same shape as <see cref="DocRecord"/> plus an extra data property, used to trigger schema drift.</summary>
    sealed class DocRecordWithExtraProperty {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData(IsIndexed = true)] public string Category { get; set; } = "";

        [VectorStoreData(IsIndexed = true)] public List<string> Tags { get; set; } = [];

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreData] public string Extra { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }
}