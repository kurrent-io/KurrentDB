using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Crud;

/// <summary>
/// Integration tests for <see cref="DuckDBCollection{TKey,TRecord}"/>'s record CRUD operations
/// (<c>UpsertAsync</c>, <c>GetAsync</c>, <c>DeleteAsync</c>). Each test drives a real DuckDB +
/// <c>lance</c> connection against a temporary storage directory, so the class is gated by
/// <see cref="LanceRequiredAttribute"/>.
/// </summary>
[LanceRequired]
[Category("Crud")]
public class DuckDBCollectionCrudTests {
    [Test]
    public async ValueTask upsert_new_record_then_get_with_vectors_returns_it() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            var record = new DocRecord {
                Id       = "doc1",
                Category = "science",
                Tags     = ["alpha", "beta"],
                Content  = "hello world",
                Vec      = new([1f, 2f, 3f, 4f])
            };

            await collection.UpsertAsync(record);

            var got = await collection.GetAsync("doc1", new() { IncludeVectors = true });

            await Assert.That(got).IsNotNull();
            await Assert.That(got!.Id).IsEqualTo("doc1");
            await Assert.That(got.Category).IsEqualTo("science");
            await Assert.That(got.Content).IsEqualTo("hello world");
            await Assert.That(got.Tags).IsEquivalentTo(new List<string> { "alpha", "beta" });
            await Assert.That(got.Vec.ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async ValueTask upsert_existing_key_updates_the_record() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
                new DocRecord {
                    Id       = "doc1",
                    Category = "science",
                    Tags     = ["alpha"],
                    Content  = "first",
                    Vec      = new([1f, 2f, 3f, 4f])
                });

            // Upsert the SAME key with entirely different data.
            await collection.UpsertAsync(
                new DocRecord {
                    Id       = "doc1",
                    Category = "history",
                    Tags     = ["gamma", "delta"],
                    Content  = "second",
                    Vec      = new([9f, 8f, 7f, 6f])
                });

            var got = await collection.GetAsync("doc1", new() { IncludeVectors = true });

            await Assert.That(got).IsNotNull();
            await Assert.That(got!.Category).IsEqualTo("history");
            await Assert.That(got.Content).IsEqualTo("second");
            await Assert.That(got.Tags).IsEquivalentTo(new List<string> { "gamma", "delta" });
            await Assert.That(got.Vec.ToArray()).IsEquivalentTo(new[] { 9f, 8f, 7f, 6f });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async ValueTask get_missing_key_is_null_and_default_options_leaves_vector_default() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            // Missing key -> null.
            var missing = await collection.GetAsync("nope");
            await Assert.That(missing).IsNull();

            await collection.UpsertAsync(
                new DocRecord {
                    Id       = "doc1",
                    Category = "science",
                    Tags     = ["alpha"],
                    Content  = "hello",
                    Vec      = new([1f, 2f, 3f, 4f])
                });

            // Default options: IncludeVectors defaults to false, so the vector property stays at its default.
            var got = await collection.GetAsync("doc1");

            await Assert.That(got).IsNotNull();
            await Assert.That(got!.Category).IsEqualTo("science");
            await Assert.That(got.Content).IsEqualTo("hello");
            await Assert.That(got.Vec.Length).IsEqualTo(0);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async ValueTask batch_upsert_then_get_by_keys_returns_only_present_records() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() {
                    Id       = "k1",
                    Category = "c1",
                    Tags     = ["a"],
                    Content  = "one",
                    Vec      = new([1f, 1f, 1f, 1f])
                },
                new() {
                    Id       = "k2",
                    Category = "c2",
                    Tags     = ["b"],
                    Content  = "two",
                    Vec      = new([2f, 2f, 2f, 2f])
                },
                new() {
                    Id       = "k3",
                    Category = "c3",
                    Tags     = ["c"],
                    Content  = "three",
                    Vec      = new([3f, 3f, 3f, 3f])
                }
            ]);

            var got = new List<DocRecord>();

            await foreach (var record in collection.GetAsync(["k1", "k3", "missing"], new() { IncludeVectors = true }))
                got.Add(record);

            await Assert.That(got.Count).IsEqualTo(2);

            var byId = got.ToDictionary(keySelector: r => r.Id, StringComparer.Ordinal);
            await Assert.That(byId.ContainsKey("k1")).IsTrue();
            await Assert.That(byId.ContainsKey("k3")).IsTrue();
            await Assert.That(byId["k1"].Content).IsEqualTo("one");
            await Assert.That(byId["k3"].Vec.ToArray()).IsEquivalentTo(new[] { 3f, 3f, 3f, 3f });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async ValueTask delete_existing_and_missing_keys_behaves_as_specified() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() {
                    Id       = "k1",
                    Category = "c1",
                    Tags     = ["a"],
                    Content  = "one",
                    Vec      = new([1f, 1f, 1f, 1f])
                },
                new() {
                    Id       = "k2",
                    Category = "c2",
                    Tags     = ["b"],
                    Content  = "two",
                    Vec      = new([2f, 2f, 2f, 2f])
                }
            ]);

            // Delete an existing record -> subsequent Get is null.
            await collection.DeleteAsync("k1");
            await Assert.That(await collection.GetAsync("k1")).IsNull();

            // Delete a missing key -> no exception (no-op).
            await collection.DeleteAsync("does-not-exist");

            // Batch delete of [present, missing] -> present gone, no throw.
            await collection.DeleteAsync(["k2", "also-missing"]);
            await Assert.That(await collection.GetAsync("k2")).IsNull();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async ValueTask dynamic_collection_round_trips_dictionary_record() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);

            // A definition mirroring the DocRecord POCO: PascalCase model names (the dictionary keys),
            // lowercase storage names (the columns) — see the note on DocRecord about the lance DELETE bug.
            var definition = new VectorStoreCollectionDefinition {
                Properties = [
                    new VectorStoreKeyProperty("Id", typeof(string)) { StorageName                       = "id" },
                    new VectorStoreDataProperty("Category", typeof(string)) { StorageName                = "category" },
                    new VectorStoreDataProperty("Tags", typeof(List<string>)) { StorageName              = "tags" },
                    new VectorStoreDataProperty("Content", typeof(string)) { StorageName                 = "content" },
                    new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4) { StorageName = "vec" }
                ]
            };

            var collection = store.GetDynamicCollection("dyn", definition);
            await collection.EnsureCollectionExistsAsync();

            // Dynamic dictionaries are keyed by property ModelName.
            var record = new Dictionary<string, object?> {
                ["Id"]       = "d1",
                ["Category"] = "science",
                ["Tags"]     = new List<string> { "x", "y" },
                ["Content"]  = "dynamic",
                ["Vec"]      = new ReadOnlyMemory<float>([5f, 6f, 7f, 8f])
            };

            await collection.UpsertAsync(record);

            var got = await collection.GetAsync("d1", new() { IncludeVectors = true });

            await Assert.That(got).IsNotNull();
            await Assert.That((string)got!["Id"]!).IsEqualTo("d1");
            await Assert.That((string)got["Category"]!).IsEqualTo("science");
            await Assert.That((string)got["Content"]!).IsEqualTo("dynamic");
            await Assert.That((List<string>)got["Tags"]!).IsEquivalentTo(new List<string> { "x", "y" });
            await Assert.That(((ReadOnlyMemory<float>)got["Vec"]!).ToArray()).IsEquivalentTo(new[] { 5f, 6f, 7f, 8f });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async ValueTask scalar_record_round_trips_int_double_bool_datetime() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, ScalarRecord>("scalars");
            await collection.EnsureCollectionExistsAsync();

            var createdAt = new DateTime(
                2024, 5, 6,
                7, 8, 9);

            var record = new ScalarRecord {
                Id        = "s1",
                IntVal    = 42,
                DoubleVal = 2.5,
                BoolVal   = true,
                CreatedAt = createdAt
            };

            await collection.UpsertAsync(record);

            var got = await collection.GetAsync("s1");

            await Assert.That(got).IsNotNull();
            await Assert.That(got!.IntVal).IsEqualTo(42);
            await Assert.That(got.DoubleVal).IsEqualTo(2.5);
            await Assert.That(got.BoolVal).IsTrue();
            await Assert.That(got.CreatedAt).IsEqualTo(createdAt);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-crud-test-" + Guid.NewGuid().ToString("N"));
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

    // NOTE: Storage (column) names are deliberately lowercase. The DuckDB `lance` extension (as of
    // version 533e0ee on DuckDB 1.5.3) has a broken DELETE predicate pushdown for columns whose names
    // contain uppercase letters: `DELETE ... WHERE Id = ?` silently matches zero rows even though the
    // identical SELECT matches, so the row is never removed. INSERT/MERGE and SELECT are unaffected.
    // Lowercase storage names (which also match the golden MERGE oracle's convention) avoid the bug and
    // let these tests exercise the real delete path. Storage names must also avoid reserved words, since
    // the schema builder emits unquoted identifiers.
    sealed class DocRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags")]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class ScalarRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "int_val")]
        public int IntVal { get; set; }

        [VectorStoreData(StorageName = "double_val")]
        public double DoubleVal { get; set; }

        [VectorStoreData(StorageName = "bool_val")]
        public bool BoolVal { get; set; }

        [VectorStoreData(StorageName = "created_at")]
        public DateTime CreatedAt { get; set; }
    }
}