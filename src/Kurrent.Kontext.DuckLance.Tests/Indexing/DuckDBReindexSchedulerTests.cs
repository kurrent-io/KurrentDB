using System.Data.Common;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Indexing;

/// <summary>
/// Unit tests for <see cref="DuckDBReindexDecision.Decide"/>: the pure, database-free tick decision. No DuckDB or
/// <c>lance</c> connection is touched anywhere in this class, so it is not gated by <see cref="LanceRequiredAttribute"/>.
/// </summary>
public class DuckDBReindexDecisionTests {
    static readonly DateTimeOffset s_now = new(
        2026, 1, 1,
        0, 0, 0,
        TimeSpan.Zero);

    [Test]
    public async Task Decide_BelowRatio_NoIndexOperations() {
        // unindexed = 1000 (meets the floor) but ratio = 1000 / 100000 = 0.01, below the 0.15 threshold.
        var decision = Decide(
            100000,
            Vectors(("vec_idx", 99000)),
            Options(0.15, 1000),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(0);
        await Assert.That(decision.RetrainRan).IsFalse();
    }

    [Test]
    public async Task Decide_AboveRatioButBelowFloor_NoIndexOperations() {
        // ratio = 50 / 100 = 0.5 (well above 0.15) but unindexed = 50 is below the 1000-row floor.
        var decision = Decide(
            100,
            Vectors(("vec_idx", 50)),
            Options(0.15, 1000),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Decide_BothThresholdsExceeded_AppendsPerIndex() {
        // ratio = 5000 / 10000 = 0.5 (> 0.15) AND unindexed = 5000 (>= 1000): append.
        var decision = Decide(
            10000,
            Vectors(("vec_idx", 5000)),
            Options(0.15, 1000),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(1);
        await Assert.That(decision.IndexOperations[0].IndexName).IsEqualTo("vec_idx");
        await Assert.That(decision.IndexOperations[0].Action).IsEqualTo(DuckDBReindexIndexAction.Append);
        await Assert.That(decision.RetrainRan).IsFalse();
    }

    [Test]
    public async Task Decide_RetrainDue_RetrainsEveryIndex_AndTakesPrecedenceOverAppend() {
        // The same snapshot that would append above, but a retrain is due: every vector index is retrained instead.
        var decision = Decide(
            10000,
            Vectors(("vec_idx", 5000)),
            Options(0.15, 1000),
            true);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(1);
        await Assert.That(decision.IndexOperations[0].Action).IsEqualTo(DuckDBReindexIndexAction.Retrain);
        await Assert.That(decision.RetrainRan).IsTrue();
    }

    [Test]
    public async Task Decide_ZeroRows_NoIndexOperations_ButCompactAndVacuumStillRun() {
        var decision = Decide(
            0,
            Vectors(("vec_idx", 0)),
            Options(0.15, 1000),
            true);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(0);
        await Assert.That(decision.RetrainRan).IsFalse();
        await Assert.That(decision.Compact).IsTrue();
        await Assert.That(decision.Vacuum).IsTrue();
    }

    [Test]
    public async Task Decide_NoVectorIndexes_NoIndexOperations_ButCompactAndVacuumStillRun() {
        var decision = Decide(
            1000,
            Vectors(),
            Options(0.15, 1),
            true);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(0);
        await Assert.That(decision.RetrainRan).IsFalse();
        await Assert.That(decision.Compact).IsTrue();
        await Assert.That(decision.Vacuum).IsTrue();
    }

    [Test]
    public async Task Decide_MultipleVectorIndexes_EachDecidedIndependently() {
        // index "a" has a 5000-row backlog (append); index "b" has a 100-row backlog (ratio 0.01, no-op).
        var decision = Decide(
            10000,
            Vectors(("a_idx", 5000), ("b_idx", 9900)),
            Options(0.15, 1000),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(1);
        await Assert.That(decision.IndexOperations[0].IndexName).IsEqualTo("a_idx");
        await Assert.That(decision.IndexOperations[0].Action).IsEqualTo(DuckDBReindexIndexAction.Append);
    }

    [Test]
    public async Task Decide_RatioExactlyAtThreshold_DoesNotAppend() {
        // unindexed = 500, ratio = 500 / 1000 = 0.5 exactly; the ratio test is strict '>', so this is a no-op.
        var decision = Decide(
            1000,
            Vectors(("vec_idx", 500)),
            Options(0.5, 1),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Decide_RatioJustAboveThreshold_Appends() {
        // unindexed = 501, ratio = 0.501 > 0.5: the strict '>' now passes.
        var decision = Decide(
            1000,
            Vectors(("vec_idx", 499)),
            Options(0.5, 1),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(1);
        await Assert.That(decision.IndexOperations[0].Action).IsEqualTo(DuckDBReindexIndexAction.Append);
    }

    [Test]
    public async Task Decide_UnindexedExactlyAtFloor_Appends() {
        // unindexed = 100 exactly at the floor; the floor test is '>=', so this triggers. ratio 0.5 > 0.1 passes.
        var decision = Decide(
            200,
            Vectors(("vec_idx", 100)),
            Options(0.1, 100),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(1);
        await Assert.That(decision.IndexOperations[0].Action).IsEqualTo(DuckDBReindexIndexAction.Append);
    }

    [Test]
    public async Task Decide_UnindexedJustBelowFloor_DoesNotAppend() {
        // unindexed = 99, one below the floor of 100; the '>=' floor test fails.
        var decision = Decide(
            200,
            Vectors(("vec_idx", 101)),
            Options(0.1, 100),
            false);

        await Assert.That(decision.IndexOperations.Count).IsEqualTo(0);
    }

    // --- Fixtures ---

    static DuckDBReindexDecision Decide(
        long totalRows,
        IReadOnlyList<(string IndexName, string Fields, long RowsIndexed)> vectorIndexes,
        DuckDBReindexOptions options,
        bool retrainDue
    ) {
        // Drive retrain-due purely off the clock inputs: when due, place the last retrain one interval + a day in
        // the past; when not, place it exactly at "now" (zero elapsed).
        var lastRetrain = retrainDue ? s_now - options.RetrainInterval - TimeSpan.FromDays(1) : s_now;

        return DuckDBReindexDecision.Decide(
            totalRows, vectorIndexes, options,
            lastRetrain, s_now);
    }

    static DuckDBReindexOptions Options(double ratio, int floor) =>
        new() {
            UnindexedRatioThreshold = ratio,
            UnindexedRowFloor       = floor,
            RetrainInterval         = TimeSpan.FromHours(24)
        };

    static List<(string IndexName, string Fields, long RowsIndexed)> Vectors(params (string Name, long RowsIndexed)[] rows) {
        var list = new List<(string IndexName, string Fields, long RowsIndexed)>(rows.Length);

        foreach (var (name, rowsIndexed) in rows)
            list.Add((name, "vec", rowsIndexed));

        return list;
    }
}

/// <summary>
/// Registry and lifecycle tests for the store-owned reindex schedulers. These exercise only registration and
/// disposal (no DuckDB/<c>lance</c> work happens until a tick actually runs against a real table), so they are
/// not gated by <see cref="LanceRequiredAttribute"/>.
/// </summary>
public class DuckDBReindexSchedulerRegistryTests {
    [Test]
    public async Task GetCollection_SameName_RegistersExactlyOneScheduler_DifferentNames_RegisterSeparate() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);

            _ = store.GetCollection<string, SchedulerRecord>("docs");
            _ = store.GetCollection<string, SchedulerRecord>("docs");
            await Assert.That(store.SchedulerCount).IsEqualTo(1);

            _ = store.GetCollection<string, SchedulerRecord>("other");
            await Assert.That(store.SchedulerCount).IsEqualTo(2);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task GetDynamicCollection_RegistersAScheduler_ButStoreProxyOperationsDoNot() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);

            // The schema-agnostic store-level proxy operations build a throwaway general-purpose collection and
            // must NOT register a scheduler for it.
            _ = await store.CollectionExistsAsync("docs");
            await Assert.That(store.SchedulerCount).IsEqualTo(0);

            // An explicit dynamic collection request, however, does register one.
            _ = store.GetDynamicCollection("docs", DynamicDefinition());
            await Assert.That(store.SchedulerCount).IsEqualTo(1);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task StoreDispose_DisposesSchedulers_SubsequentTickNowIsSafeNoOp() {
        var dir   = CreateTempStorageDir();
        var store = NewStore(dir);

        try {
            var collection = store.GetCollection<string, SchedulerRecord>("docs");
            var scheduler  = store.GetScheduler(collection.DatasetPath);

            store.Dispose();

            // The registry is cleared and the scheduler is disposed; a subsequent tick must be a safe no-op that
            // never throws (chosen over throwing ObjectDisposedException).
            await scheduler.TickNowAsync();

            await Assert.That(store.SchedulerCount).IsEqualTo(0);
        } finally {
            store.Dispose();
            TryDeleteDir(dir);
        }
    }

    static VectorStoreCollectionDefinition DynamicDefinition() =>
        new() {
            Properties = [
                new VectorStoreKeyProperty("id", typeof(string)) { StorageName                       = "id" },
                new VectorStoreVectorProperty("vec", typeof(ReadOnlyMemory<float>), 8) { StorageName = "vec" }
            ]
        };

    static DuckDBVectorStore NewStore(string dir) =>
        new(
            new() {
                DatabasePath   = Path.Combine(dir, "duck.db"),
                ReindexOptions = new() { TickInterval = TimeSpan.FromHours(1) }
            });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-reindex-registry-test-" + Guid.NewGuid().ToString("N"));
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

    sealed class SchedulerRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreVector(8, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}

/// <summary>
/// Integration tests for <see cref="DuckDBReindexScheduler"/> and <see cref="DuckDBCollection{TKey, TRecord}.OptimizeIndexesAsync"/>
/// against a real DuckDB + <c>lance</c> connection over a temporary storage directory, so the class is gated by
/// <see cref="LanceRequiredAttribute"/>. Ticks are driven deterministically via <see cref="DuckDBReindexScheduler.TickNowAsync"/>
/// except for the single real-timer smoke test, which polls with a generous, eventually-consistent timeout.
/// </summary>
[LanceRequired]
public class DuckDBReindexSchedulerIntegrationTests {
    [Test]
    public async Task OptimizeIndexesAsync_FoldsRowsInsertedPastTheIndex_RowsIndexedCatchesUp() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SchedulerRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 300,
                "r");

            var created = await collection.EnsureVectorIndexesAsync();
            await Assert.That(created).IsEqualTo(1);

            // Oracle: the freshly created IVF_PQ index reports all 300 rows as indexed.
            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(300L);

            // 50 more rows land via raw bulk INSERT — they are NOT yet folded into the index.
            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 50,
                "s");

            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(300L);

            // OptimizeIndexesAsync append-optimizes the vector index: rows_indexed catches up to 350.
            await collection.OptimizeIndexesAsync();
            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(350L);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task TickNowAsync_AppendsBacklog_ThenIsNoOp_AndDatasetStaysSearchable() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir, EagerReindex());
            var collection = store.GetCollection<string, SchedulerRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 300,
                "r");

            await collection.EnsureVectorIndexesAsync();
            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(300L);

            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 50,
                "s"); // total 350, backlog 50

            var scheduler = store.GetScheduler(collection.DatasetPath);

            // One tick folds the 50-row backlog in end to end (append + compaction + vacuum), no exception.
            await scheduler.TickNowAsync();
            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(350L);

            // A second tick has nothing to append (already fresh): it is a no-op that also does not throw.
            await scheduler.TickNowAsync();
            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(350L);

            // The dataset remains searchable after compaction + vacuum ran.
            var results = await SearchToListAsync(collection.SearchAsync(new ReadOnlyMemory<float>(UniformVector()), 5));
            await Assert.That(results.Count).IsGreaterThan(0);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task TickNowAsync_NoPriorEnsureVectorIndexes_CreatesTheVectorIndexViaCatchUp_ThenAppends() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir, EagerReindex());
            var collection = store.GetCollection<string, SchedulerRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            // 300 rows, but EnsureVectorIndexesAsync is deliberately never called: no vector index exists yet.
            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 300,
                "r");

            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsNull();

            var scheduler = store.GetScheduler(collection.DatasetPath);

            // Catch-up: the tick creates the missing vector index via the collection's EnsureVectorIndexes delegate.
            await scheduler.TickNowAsync();
            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(300L);

            // And the append path works after creation: a later backlog is folded in on the next tick.
            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 50,
                "s");

            await scheduler.TickNowAsync();
            await Assert.That(await VecIndexRowsIndexedAsync(store, collection.DatasetPath)).IsEqualTo(350L);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task TickNowAsync_TableNeverCreated_DoesNotThrow() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SchedulerRecord>("docs");

            // EnsureCollectionExistsAsync is deliberately NOT called, so the table (and dataset) does not exist.
            var scheduler = store.GetScheduler(collection.DatasetPath);

            // A tick against a never-created collection must skip quietly, not throw.
            await scheduler.TickNowAsync();

            // The tick created nothing: the collection still does not exist.
            await Assert.That(await collection.CollectionExistsAsync()).IsFalse();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task RealTimer_EventuallyCreatesTheIndexAndLandsTheAppend() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            // A fast (200ms) real timer with eager thresholds, so a background tick both creates the index and,
            // later, folds a backlog in without any explicit TickNowAsync call.
            var reindex = new DuckDBReindexOptions {
                UnindexedRowFloor       = 1,
                UnindexedRatioThreshold = 0.01,
                TickInterval            = TimeSpan.FromMilliseconds(200)
            };

            store = NewStore(dir, reindex);
            var collection = store.GetCollection<string, SchedulerRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 300,
                "r");

            // Phase 1: the real timer catches up and creates the vector index at 300 rows.
            var created = await PollAsync(
                read: () => VecIndexRowsIndexedAsync(store, collection.DatasetPath),
                predicate: value => value == 300L,
                TimeSpan.FromSeconds(5));

            await Assert.That(created).IsTrue();

            // Phase 2: a fresh backlog is folded in by a later real-timer tick, landing rows_indexed at 350.
            await SeedBulkRowsAsync(
                store, collection.QualifiedTableName, 50,
                "s");

            var appended = await PollAsync(
                read: () => VecIndexRowsIndexedAsync(store, collection.DatasetPath),
                predicate: value => value == 350L,
                TimeSpan.FromSeconds(5));

            await Assert.That(appended).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // --- Helpers ---

    /// <summary>Reads the <c>rows_indexed</c> of the vector index on the "vec" column, or <see langword="null"/> when none exists.</summary>
    static async Task<long?> VecIndexRowsIndexedAsync(DuckDBVectorStore store, string datasetPath) {
        var indexes = await ShowIndexesAsync(store, datasetPath);

        foreach (var index in indexes) {
            if (DuckDBMaintenanceSql.IsVectorIndexType(index.Type)
             && string.Equals(index.Fields, "vec", StringComparison.OrdinalIgnoreCase))
                return index.RowsIndexed;
        }

        return null;
    }

    static Task<IReadOnlyList<(string Name, string Type, string Fields, long RowsIndexed)>> ShowIndexesAsync(DuckDBVectorStore store, string datasetPath) =>
        store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SHOW INDEXES ON '{EscapePath(datasetPath)}'";
                using var reader = command.ExecuteReader();
                return DuckDBIndexBuilder.ParseShowIndexes(reader);
            },
            CancellationToken.None);

    /// <summary>
    /// Bulk-seeds <paramref name="count"/> filler rows directly via SQL (bypassing the ORM's per-row MERGE) with
    /// an explicit column list, so the seed does not depend on the model's column ordering. The table shape is
    /// <see cref="SchedulerRecord"/>'s: <c>id VARCHAR, category VARCHAR, content VARCHAR, vec FLOAT[8]</c>.
    /// </summary>
    static Task SeedBulkRowsAsync(DuckDBVectorStore store, string qualifiedTableName, int count, string idPrefix) =>
        store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();

                command.CommandText =
                    $"INSERT INTO {qualifiedTableName} (id, category, content, vec) "
                  + $"SELECT '{idPrefix}' || i, 'cat', 'filler ' || i, "
                  + "[0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8, "
                  + "0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8]::FLOAT[8] "
                  + $"FROM range({count}) t(i)";

                command.ExecuteNonQuery();
            },
            CancellationToken.None);

    /// <summary>Polls <paramref name="read"/> until <paramref name="predicate"/> holds or <paramref name="timeout"/> elapses.</summary>
    static async Task<bool> PollAsync(Func<Task<long?>> read, Func<long?, bool> predicate, TimeSpan timeout) {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline) {
            var value = await TryReadAsync(read);

            if (predicate(value))
                return true;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return predicate(await TryReadAsync(read));
    }

    static async Task<long?> TryReadAsync(Func<Task<long?>> read) {
        try {
            return await read();
        } catch (DbException) {
            // A concurrent maintenance tick (compaction/vacuum) can momentarily disturb a cached dataset handle;
            // treat a transient read failure as "not ready yet" and let the next poll retry.
            return null;
        }
    }

    static async Task<List<VectorSearchResult<T>>> SearchToListAsync<T>(IAsyncEnumerable<VectorSearchResult<T>> source)
        where T : class {
        var results = new List<VectorSearchResult<T>>();

        await foreach (var result in source)
            results.Add(result);

        return results;
    }

    static float[] UniformVector() => Enumerable.Repeat(0.5f, 8).ToArray();

    static DuckDBReindexOptions EagerReindex() =>
        new() {
            // Fire on the smallest possible backlog, and never let a real timer tick interfere with the
            // deterministic TickNowAsync-driven assertions.
            UnindexedRowFloor       = 1,
            UnindexedRatioThreshold = 0.01,
            TickInterval            = TimeSpan.FromHours(1)
        };

    static string EscapePath(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    static DuckDBVectorStore NewStore(string dir, DuckDBReindexOptions? reindex = null) =>
        new(
            new() {
                DatabasePath   = Path.Combine(dir, "duck.db"),
                ReindexOptions = reindex ?? new DuckDBReindexOptions { TickInterval = TimeSpan.FromHours(1) }
            });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-reindex-scheduler-test-" + Guid.NewGuid().ToString("N"));
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
    // lance extension's broken DELETE predicate pushdown for uppercase column names. IndexKind is left at its
    // default (null -> IVF_PQ) so the scheduler exercises the real provider default vector index.
    sealed class SchedulerRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "content", IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(8, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}