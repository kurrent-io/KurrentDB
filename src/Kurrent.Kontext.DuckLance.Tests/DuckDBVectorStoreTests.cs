using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests;

/// <summary>
/// Unit and integration tests for <see cref="DuckDBVectorStore"/>: construction validation, collection
/// resolution (typed and dynamic), service discovery, collection-name listing, and disposal.
/// </summary>
public class DuckDBVectorStoreTests {
    // --- Constructor validation ---

    [Test]
    public async Task Constructor_EmptyDatabasePath_ThrowsArgumentException() {
        var options = new DuckDBVectorStoreOptions { DatabasePath = "" };

        await Assert
            .That(() => { new DuckDBVectorStore(options); })
            .Throws<ArgumentException>();
    }

    // --- GetCollection (typed) ---

    [Test]
    public async Task GetCollection_ValidRecord_ReturnsConfiguredCollection() {
        using var store      = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });
        using var collection = store.GetCollection<string, DocRecord>("docs");

        await Assert.That(collection.Name).IsEqualTo("docs");
        await Assert.That(collection.TableName).IsEqualTo("docs");
        await Assert.That(collection.QualifiedTableName).IsEqualTo("ldb.main.docs");
        await Assert.That(collection.Model.KeyProperty).IsNotNull();
    }

    // --- GetCollection key/record-shape gating ---

    [Test]
    public async Task GetCollection_IntKey_ThrowsNotSupportedException() {
        using var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });

        await Assert
            .That(() => { store.GetCollection<int, DocRecord>("docs"); })
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task GetCollection_DictionaryRecord_ThrowsArgumentException() {
        using var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });

        await Assert
            .That(() => { store.GetCollection<string, Dictionary<string, object?>>("docs"); })
            .Throws<ArgumentException>();
    }

    // --- GetDynamicCollection ---

    [Test]
    public async Task GetDynamicCollection_ValidDefinition_ReturnsCollection() {
        using var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });

        VectorStoreCollectionDefinition definition = new() {
            Properties = [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Category", typeof(string)),
                new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4)
            ]
        };

        using var collection = store.GetDynamicCollection("docs", definition);

        await Assert.That(collection.Name).IsEqualTo("docs");
        await Assert.That(collection.Model.KeyProperty).IsNotNull();
    }

    // --- Invalid collection name ---

    [Test]
    public async Task GetCollection_InvalidCollectionName_ThrowsArgumentException() {
        using var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });

        await Assert
            .That(() => { store.GetCollection<string, DocRecord>("my.docs"); })
            .Throws<ArgumentException>();
    }

    // --- GetService (store) ---

    [Test]
    public async Task GetService_VectorStoreMetadataType_ReturnsMetadata() {
        using var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });

        var service = store.GetService(typeof(VectorStoreMetadata));

        await Assert.That(service).IsNotNull();
        await Assert.That(service is VectorStoreMetadata).IsTrue();
    }

    [Test]
    public async Task GetService_StoreType_ReturnsSameInstance() {
        using var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });

        var service = store.GetService(typeof(DuckDBVectorStore));

        await Assert.That(ReferenceEquals(service, store)).IsTrue();
    }

    [Test]
    public async Task GetService_NonNullServiceKey_ReturnsNull() {
        using var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });

        var service = store.GetService(typeof(VectorStoreMetadata), "some-key");

        await Assert.That(service).IsNull();
    }

    // --- GetService (collection) ---

    [Test]
    public async Task Collection_GetService_VectorStoreCollectionMetadataType_ReturnsMetadata() {
        using var store      = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });
        using var collection = store.GetCollection<string, DocRecord>("docs");

        var service = collection.GetService(typeof(VectorStoreCollectionMetadata));

        await Assert.That(service).IsNotNull();
        await Assert.That(service is VectorStoreCollectionMetadata).IsTrue();
    }

    [Test]
    public async Task Collection_GetService_CollectionType_ReturnsSameInstance() {
        using var store      = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });
        using var collection = store.GetCollection<string, DocRecord>("docs");

        var service = collection.GetService(collection.GetType());

        await Assert.That(ReferenceEquals(service, collection)).IsTrue();
    }

    [Test]
    public async Task Collection_GetService_NonNullServiceKey_ReturnsNull() {
        using var store      = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") });
        using var collection = store.GetCollection<string, DocRecord>("docs");

        var service = collection.GetService(typeof(VectorStoreCollectionMetadata), "some-key");

        await Assert.That(service).IsNull();
    }

    // --- ListCollectionNamesAsync (integration) ---

    [Test]
    [LanceRequired]
    public async Task ListCollectionNamesAsync_ReturnsOnlyThisStoresCollections() {
        var                dir    = CreateTempStorageDir();
        DuckDBVectorStore? storeA = null;
        DuckDBVectorStore? storeB = null;

        try {
            storeA = new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

            await storeA.ConnectionManager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"CREATE TABLE {storeA.Resolver.GetQualifiedTableName("docs")} (id VARCHAR)";
                    command.ExecuteNonQuery();
                },
                CancellationToken.None);

            var namesA = new List<string>();

            await foreach (var name in storeA.ListCollectionNamesAsync())
                namesA.Add(name);

            await Assert.That(namesA.Count).IsEqualTo(1);
            await Assert.That(namesA[0]).IsEqualTo("docs");

            // A store over a different database directory must not see storeA's collections.
            storeB = new(new() { DatabasePath = Path.Combine(dir, "other", "duck.db") });

            var namesB = new List<string>();

            await foreach (var name in storeB.ListCollectionNamesAsync())
                namesB.Add(name);

            await Assert.That(namesB.Count).IsEqualTo(0);
        } finally {
            storeA?.Dispose();
            storeB?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // --- Dispose ---

    [Test]
    [LanceRequired]
    public async Task Dispose_KeepsStableEngineFile_AndIsIdempotent() {
        var dir   = CreateTempStorageDir();
        var store = new DuckDBVectorStore(new() { DatabasePath = Path.Combine(dir, "duck.db") });

        try {
            var dbPath = store.ConnectionManager.DatabasePath;
            await Assert.That(dbPath).IsEqualTo(Path.Combine(Path.GetFullPath(dir), "duck.db"));

            // Open a connection so the stable DuckDB engine file is actually created on disk.
            await store.ConnectionManager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    command.ExecuteScalar();
                },
                CancellationToken.None);

            await Assert.That(File.Exists(dbPath)).IsTrue();

            store.Dispose();

            // The engine file is durable state: it SURVIVES Dispose (checkpointed, never deleted).
            await Assert.That(File.Exists(dbPath)).IsTrue();

            // Double-dispose must be harmless.
            store.Dispose();
        } finally {
            store.Dispose();
            TryDeleteDir(dir);
        }
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-store-test-" + Guid.NewGuid().ToString("N"));
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

        [VectorStoreData] public string Category { get; set; } = "";

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }
}