using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using TUnit.Assertions.Enums;

namespace DuckLance.Tests.Crud;

/// <summary>
/// Integration tests for <see cref="DuckDBCollection{TKey,TRecord}.QueryAsync"/>: the relational
/// escape hatch for shapes the portable surface cannot express. Each test drives a real DuckDB +
/// <c>lance</c> connection against a temporary storage directory, so the class is gated by
/// <see cref="LanceRequiredAttribute"/>.
/// </summary>
[LanceRequired]
public class DuckDBQueryTests {
    [Test]
    public async Task Query_Combines_In_Set_Containment_Ordering_And_Limit_In_One_Statement() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, NoteRecord>("notes");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync([
                new() { Id = "n1", Kind = 1, Rank = 10, Tags = ["a", "b"], Vec = new([1f, 0f, 0f, 0f]) },
                new() { Id = "n2", Kind = 2, Rank = 30, Tags = ["a", "b", "c"], Vec = new([0f, 1f, 0f, 0f]) },
                new() { Id = "n3", Kind = 2, Rank = 20, Tags = ["a", "b"], Vec = new([0f, 0f, 1f, 0f]) },
                new() { Id = "n4", Kind = 3, Rank = 40, Tags = ["a", "b"], Vec = new([0f, 0f, 0f, 1f]) }, // excluded: kind not in set
                new() { Id = "n5", Kind = 1, Rank = 50, Tags = ["a"], Vec = new([1f, 1f, 0f, 0f]) }       // excluded: missing tag b
            ]);

            // The exact relational shape the portable surface cannot express in one round trip:
            // an any-of set, an ALL-tags containment, server-side ordering, and a limit.
            var results = await collection.QueryAsync(
                "WHERE kind IN (?, ?) AND array_has_all(tags, CAST(? AS VARCHAR[])) ORDER BY rank DESC LIMIT 2",
                [1, 2, new List<string> { "a", "b" }]);

            await Assert.That(results.Select(r => r.Id)).IsEquivalentTo(["n2", "n3"], CollectionOrdering.Matching);

            // Lean decode (the default): every non-vector column populated, the vector left default.
            await Assert.That(results[0].Kind).IsEqualTo(2);
            await Assert.That(results[0].Rank).IsEqualTo(30);
            await Assert.That(results[0].Tags).IsEquivalentTo(["a", "b", "c"], CollectionOrdering.Matching);
            await Assert.That(results[0].Vec.Length).IsEqualTo(0);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task Query_With_Vectors_Decodes_The_Vector_Columns_Too() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, NoteRecord>("notes_vec");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(new NoteRecord { Id = "n1", Kind = 1, Rank = 1, Tags = ["a"], Vec = new([1f, 2f, 3f, 4f]) });

            var results = await collection.QueryAsync("WHERE id = ?", ["n1"], includeVectors: true);

            await Assert.That(results.Count).IsEqualTo(1);
            await Assert.That(results[0].Vec.ToArray()).IsEquivalentTo(new[] { 1f, 2f, 3f, 4f }, CollectionOrdering.Matching);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-query-test-" + Guid.NewGuid().ToString("N"));
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

    sealed class NoteRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public int Kind { get; set; }

        [VectorStoreData] public int Rank { get; set; }

        [VectorStoreData] public List<string> Tags { get; set; } = [];

        [VectorStoreVector(4)] public ReadOnlyMemory<float> Vec { get; set; }
    }
}
