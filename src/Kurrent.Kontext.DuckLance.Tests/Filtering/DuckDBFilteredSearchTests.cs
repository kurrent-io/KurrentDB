using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Filtering;

/// <summary>
/// Integration tests for filtered <see cref="DuckDBCollection{TKey, TRecord}.SearchAsync{TInput}"/> and
/// <see cref="DuckDBCollection{TKey, TRecord}.GetAsync(System.Linq.Expressions.Expression{System.Func{TRecord, bool}}, int, FilteredRecordRetrievalOptions{TRecord}?, System.Threading.CancellationToken)"/>.
/// Each test drives a real DuckDB + <c>lance</c> connection against a temporary storage directory, so the class
/// is gated by <see cref="LanceRequiredAttribute"/>.
/// </summary>
/// <remarks>
/// The oracle case exists to catch a silent post-filter regression: the DuckDB <c>lance</c> extension pushes an
/// equality WHERE down as a TRUE prefilter (correct even when matches fall outside the global top-k), but silently
/// post-filters containment (<c>array_has_any</c>). The provider's oversample policy (search with <c>k =
/// count(*)</c> for any containment filter) is what keeps containment correct; a smaller <c>k</c> silently drops
/// matching rows. The oracle seeds thousands of near-query filler rows plus a few far-away matching rows so that a
/// naive small-<c>k</c> search would return zero matches.
/// </remarks>
[LanceRequired]
public class DuckDBFilteredSearchTests {
    static ReadOnlyMemory<float> Query() => new([1f, 0f, 0f, 0f]);

    // 1. Equality narrows to the matching category.
    [Test]
    public async Task Search_EqualityFilter_NarrowsToMatchingCategory() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FilterableRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            var records = new List<FilterableRecord>();

            for (var i = 0; i < 3; i++) {
                records.Add(NearQuery($"a_{i}", "cat_a", ["t"]));
                records.Add(NearQuery($"b_{i}", "cat_b", ["t"]));
            }

            await collection.UpsertAsync(records);

            var results = await SearchToListAsync(collection.SearchAsync(Query(), 10, new() { Filter = r => r.Category == "cat_a" }));

            await Assert.That(results.Count).IsEqualTo(3);
            await Assert.That(results.All(r => r.Record.Category == "cat_a")).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 2. THE ORACLE CASE — silent-post-filter catcher.
    // 2000 near-query filler rows + 50 far-away 'cat_post'/'post' rows. Querying [1,0,0,0]:
    //   - equality Category == "cat_post" (top 5) must return exactly 5 rows, all cat_post -> proves TRUE prefilter
    //     (a global top-5 without prefilter would be all filler, none cat_post).
    //   - containment Tags.Contains("post") (top 5) must return exactly 5 rows, all tagged 'post' -> proves the
    //     oversample covers the whole table (a k=5 search post-filtering array_has_any would return zero).
    [Test]
    public async Task Search_OracleCase_PrefilterAndOversampleBothStayCorrect() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FilterableRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            // Bulk-seed via one columnar INSERT ... SELECT per group. The provider's per-row MERGE upsert scans the
            // whole table on every row (its ON t.id = s.id has no key index), so seeding thousands of rows that way
            // is O(n^2) and impractically slow; a bulk INSERT into the same attached Lance table stores identical
            // data far faster and is what "bulk-seed" calls for. 2000 filler rows cluster tightly around the query
            // vector [1,0,0,0] (so they dominate any small-k search); 50 matching 'cat_post'/'post' rows cluster
            // around [-1,0,0,0] — FAR from the query — so a global top-k never surfaces them.
            await BulkSeedAsync(
                store, collection.QualifiedTableName, 2000,
                50);

            // Equality: the prefilter restricts the vector search to cat_post rows, so all 5 come back cat_post.
            var equalityResults = await SearchToListAsync(collection.SearchAsync(Query(), 5, new() { Filter = r => r.Category == "cat_post" }));

            await Assert.That(equalityResults.Count).IsEqualTo(5);
            await Assert.That(equalityResults.All(r => r.Record.Category == "cat_post")).IsTrue();

            // Containment: oversample to k = count(*) so DuckDB's post-filter sees every row; 5 tagged rows survive.
            var containmentResults = await SearchToListAsync(collection.SearchAsync(Query(), 5, new() { Filter = r => r.Tags.Contains("post") }));

            await Assert.That(containmentResults.Count).IsEqualTo(5);
            await Assert.That(containmentResults.All(r => r.Record.Tags.Contains("post"))).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 3. Combined && (category equality + tag containment) returns only rows satisfying BOTH predicates.
    [Test]
    public async Task Search_CombinedEqualityAndContainment_ReturnsOnlyRowsSatisfyingBoth() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FilterableRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                NearQuery("r1", "cat_a", ["x"]),     // matches both
                NearQuery("r2", "cat_a", ["y"]),     // wrong tag
                NearQuery("r3", "cat_b", ["x"]),     // wrong category
                NearQuery("r4", "cat_a", ["x", "y"]) // matches both
            ]);

            var results = await SearchToListAsync(collection.SearchAsync(Query(), 10, new() { Filter = r => r.Category == "cat_a" && r.Tags.Contains("x") }));

            await Assert.That(results.Count).IsEqualTo(2);
            await Assert.That(results.All(r => r.Record.Category == "cat_a" && r.Record.Tags.Contains("x"))).IsTrue();
            await Assert.That(results.Select(r => r.Record.Id).OrderBy(keySelector: id => id, StringComparer.Ordinal)).IsEquivalentTo(new[] { "r1", "r4" });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 4a. GetAsync equality filter over 20 records with OrderBy on the (indexed) key -> disjoint ordered pages.
    [Test]
    public async Task GetAsync_EqualityFilter_WithOrderByPaging_ReturnsDisjointOrderedPages() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FilterableRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            var records = new List<FilterableRecord>(20);

            for (var i = 0; i < 20; i++)
                records.Add(NearQuery($"id_{i:D2}", "cat_x", ["t"]));

            await collection.UpsertAsync(records);

            var page1 = await GetToListAsync(
                collection.GetAsync(
                    filter: r => r.Category == "cat_x",
                    10,
                    new() { OrderBy = o => o.Ascending(r => r.Id) }));

            var page2 = await GetToListAsync(
                collection.GetAsync(
                    filter: r => r.Category == "cat_x",
                    10,
                    new() { OrderBy = o => o.Ascending(r => r.Id), Skip = 10 }));

            await Assert.That(page1.Count).IsEqualTo(10);
            await Assert.That(page2.Count).IsEqualTo(10);

            var page1Ids = page1.Select(r => r.Id).ToList();
            var page2Ids = page2.Select(r => r.Id).ToList();

            // Disjoint, and each page is in ascending key order.
            await Assert.That(page1Ids.Intersect(page2Ids, StringComparer.Ordinal).Any()).IsFalse();
            await Assert.That(page1Ids.SequenceEqual(page1Ids.OrderBy(keySelector: id => id, StringComparer.Ordinal))).IsTrue();
            await Assert.That(page2Ids.SequenceEqual(page2Ids.OrderBy(keySelector: id => id, StringComparer.Ordinal))).IsTrue();

            // The first page holds id_00..id_09, the second id_10..id_19.
            await Assert.That(page1Ids).IsEquivalentTo(Enumerable.Range(0, 10).Select(i => $"id_{i:D2}").ToArray());
            await Assert.That(page2Ids).IsEquivalentTo(Enumerable.Range(10, 10).Select(i => $"id_{i:D2}").ToArray());
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 4b. GetAsync containment filter returns the tagged records.
    [Test]
    public async Task GetAsync_ContainmentFilter_ReturnsTaggedRecords() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FilterableRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync([NearQuery("t1", "cat_a", ["special"]), NearQuery("t2", "cat_a", ["ordinary"]), NearQuery("t3", "cat_b", ["special"])]);

            var results = await GetToListAsync(collection.GetAsync(filter: r => r.Tags.Contains("special"), 10));

            await Assert.That(results.Count).IsEqualTo(2);
            await Assert.That(results.Select(r => r.Id).OrderBy(keySelector: id => id, StringComparer.Ordinal)).IsEquivalentTo(new[] { "t1", "t3" });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 4c. GetAsync honors IncludeVectors.
    [Test]
    public async Task GetAsync_IncludeVectors_ControlsVectorHydration() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FilterableRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync([NearQuery("v1", "cat_a", ["t"])]);

            var withVectors = await GetToListAsync(collection.GetAsync(filter: r => r.Category == "cat_a", 1, new() { IncludeVectors = true }));
            await Assert.That(withVectors.Count).IsEqualTo(1);
            await Assert.That(withVectors[0].Vec.Length).IsEqualTo(4);

            var withoutVectors = await GetToListAsync(collection.GetAsync(filter: r => r.Category == "cat_a", 1, new() { IncludeVectors = false }));
            await Assert.That(withoutVectors.Count).IsEqualTo(1);
            await Assert.That(withoutVectors[0].Vec.Length).IsEqualTo(0);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 5. An unsupported filter through the PUBLIC search API throws NotSupportedException, and no rows are harmed:
    // the collection still searches correctly afterward.
    [Test]
    public async Task Search_UnsupportedFilter_ThrowsAndLeavesCollectionSearchable() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, FilterableRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync([NearQuery("u1", "cat_a", ["t"]), NearQuery("u2", "cat_b", ["t"])]);

            await Assert
                .That(async () => await SearchToListAsync(collection.SearchAsync(Query(), 3, new() { Filter = r => r.Category != "x" })))
                .Throws<NotSupportedException>();

            // No rows harmed: an ordinary (unfiltered) search still works and returns both records.
            var results = await SearchToListAsync(collection.SearchAsync(Query(), 10));
            await Assert.That(results.Count).IsEqualTo(2);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // 6. REGRESSION — lossy value-side cast must bind the evaluated value, end to end. Seeds num == 5 and num == 6
    // rows, then filters `r.Num == (int)d` with a captured double 5.9: `(int)5.9` is 5, so exactly the num == 5 rows
    // must come back. Before the fix, ExtractValue bound the pre-cast double 5.9, DuckDB evaluated `num = 5.9`, and
    // every num == 5 row was silently dropped (zero results) — the misfilter this proves is dead.
    [Test]
    public async Task Search_LossyDoubleCastEqualityFilter_ReturnsExactlyNumFiveRows() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, NumberedRecord>("nums");
            await collection.EnsureCollectionExistsAsync();

            var records = new List<NumberedRecord>();

            for (var i = 0; i < 3; i++) {
                records.Add(
                    new() {
                        Id  = $"five_{i}",
                        Num = 5,
                        Vec = new([1f, 0f, 0f, 0f])
                    });

                records.Add(
                    new() {
                        Id  = $"six_{i}",
                        Num = 6,
                        Vec = new([1f, 0f, 0f, 0f])
                    });
            }

            await collection.UpsertAsync(records);

            var d = 5.9; // (int)d == 5

            var results = await SearchToListAsync(collection.SearchAsync(Query(), 10, new() { Filter = r => r.Num == (int)d }));

            await Assert.That(results.Count).IsEqualTo(3);
            await Assert.That(results.All(r => r.Record.Num == 5)).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static FilterableRecord NearQuery(string id, string category, List<string> tags) =>
        new() {
            Id       = id,
            Category = category,
            Tags     = tags,
            Content  = "c",
            Vec      = new([1f, 0f, 0f, 0f])
        };

    // Bulk-seeds the oracle table directly (bypassing the O(n^2) per-row MERGE upsert): one INSERT ... SELECT for
    // the near-query filler group and one for the far-away matching group. Vectors are jittered around ±1 on the
    // first axis with DuckDB's random() so no two rows collide exactly, mirroring what a real corpus looks like.
    static Task BulkSeedAsync(DuckDBVectorStore store, string qualifiedTableName, int fillerCount, int matchingCount) =>
        store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using (var command = connection.CreateCommand()) {
                    command.CommandText =
                        $"INSERT INTO {qualifiedTableName} (id, category, tags, content, vec) "
                      + "SELECT 'fill_' || i::VARCHAR, 'cat_fill', ['t_fill'], 'filler', "
                      + "CAST([1.0 + random() * 0.1 - 0.05, random() * 0.1 - 0.05, random() * 0.1 - 0.05, random() * 0.1 - 0.05] AS FLOAT[4]) "
                      + $"FROM range({fillerCount}) AS t(i)";

                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand()) {
                    command.CommandText =
                        $"INSERT INTO {qualifiedTableName} (id, category, tags, content, vec) "
                      + "SELECT 'post_' || i::VARCHAR, 'cat_post', ['post'], 'posted', "
                      + "CAST([-1.0 + random() * 0.1 - 0.05, random() * 0.1 - 0.05, random() * 0.1 - 0.05, random() * 0.1 - 0.05] AS FLOAT[4]) "
                      + $"FROM range({matchingCount}) AS t(i)";

                    command.ExecuteNonQuery();
                }
            },
            CancellationToken.None);

    static async Task<List<VectorSearchResult<T>>> SearchToListAsync<T>(IAsyncEnumerable<VectorSearchResult<T>> source)
        where T : class {
        var results = new List<VectorSearchResult<T>>();

        await foreach (var result in source)
            results.Add(result);

        return results;
    }

    static async Task<List<T>> GetToListAsync<T>(IAsyncEnumerable<T> source) {
        var results = new List<T>();

        await foreach (var item in source)
            results.Add(item);

        return results;
    }

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-filter-test-" + Guid.NewGuid().ToString("N"));
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

    sealed class FilterableRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags", IsIndexed = true)]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.EuclideanSquaredDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    /// <summary>Record with a numeric IsIndexed data property, for the lossy value-side-cast regression test.</summary>
    sealed class NumberedRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "num", IsIndexed = true)]
        public int Num { get; set; }

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.EuclideanSquaredDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}