using System.Data.Common;
using System.Diagnostics;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.Hardening;

/// <summary>
/// Tests for the Stage 11 hardening items: native multi-row batch upsert (with in-batch key de-duplication),
/// the key-column BTREE index, reserved-word storage-name rejection, and score-threshold support. The pure
/// <see cref="DuckDBScoreConverter"/> direction tests need no connection and run everywhere; the end-to-end tests
/// drive a real DuckDB + <c>lance</c> connection and are gated per-method by <see cref="LanceRequiredAttribute"/>.
/// </summary>
public class DuckDBHardeningTests {
    // Timings are reported (never asserted); they are written both to the console and to a scratch file so a run
    // can surface them regardless of how the test host buffers console output.
    static readonly object s_timingLock = new();
    static readonly string s_timingPath = Path.Combine(Path.GetTempPath(), "ducklance-hardening-timings.txt");

    // ============================================================================================================
    // Item 4 — ScoreThreshold direction (pure, no DuckDB).
    // ============================================================================================================

    // Cosine distance (lower = closer): keep score <= threshold. Scores 0/1/2, threshold 1.5 keeps {0,1}.
    [Test]
    [Arguments(0.0, true)]
    [Arguments(1.0, true)]
    [Arguments(2.0, false)]
    public async Task PassesThreshold_CosineDistance_Threshold1_5_Keeps0And1(double score, bool expected) =>
        await Assert.That(DuckDBScoreConverter.PassesThreshold(DistanceFunction.CosineDistance, score, 1.5)).IsEqualTo(expected);

    // Cosine distance, threshold 0.5 keeps only {0}.
    [Test]
    [Arguments(0.0, true)]
    [Arguments(1.0, false)]
    [Arguments(2.0, false)]
    public async Task PassesThreshold_CosineDistance_Threshold0_5_KeepsOnly0(double score, bool expected) =>
        await Assert.That(DuckDBScoreConverter.PassesThreshold(DistanceFunction.CosineDistance, score, 0.5)).IsEqualTo(expected);

    // The null default is treated as cosine distance (distance semantics: keep <= threshold).
    [Test]
    [Arguments(0.0, true)]
    [Arguments(2.0, false)]
    public async Task PassesThreshold_NullDefault_UsesDistanceDirection(double score, bool expected) =>
        await Assert.That(DuckDBScoreConverter.PassesThreshold(null, score, 1.5)).IsEqualTo(expected);

    // Squared Euclidean distance is also lower = closer: keep <= threshold.
    [Test]
    [Arguments(2.0, true)]
    [Arguments(4.0, false)]
    public async Task PassesThreshold_EuclideanSquaredDistance_UsesDistanceDirection(double score, bool expected) =>
        await Assert.That(DuckDBScoreConverter.PassesThreshold(DistanceFunction.EuclideanSquaredDistance, score, 3.0)).IsEqualTo(expected);

    // Cosine similarity (higher = closer): keep score >= threshold. Scores 1/0/-1, threshold 0.5 keeps {1}.
    [Test]
    [Arguments(1.0, true)]
    [Arguments(0.0, false)]
    [Arguments(-1.0, false)]
    public async Task PassesThreshold_CosineSimilarity_Threshold0_5_KeepsOnly1(double score, bool expected) =>
        await Assert.That(DuckDBScoreConverter.PassesThreshold(DistanceFunction.CosineSimilarity, score, 0.5)).IsEqualTo(expected);

    // Dot-product similarity is also higher = closer: keep >= threshold.
    [Test]
    [Arguments(0.75, true)]
    [Arguments(0.25, false)]
    public async Task PassesThreshold_DotProductSimilarity_UsesSimilarityDirection(double score, bool expected) =>
        await Assert.That(DuckDBScoreConverter.PassesThreshold(DistanceFunction.DotProductSimilarity, score, 0.5)).IsEqualTo(expected);

    // Hybrid scores are always higher = better: keep >= threshold.
    [Test]
    [Arguments(0.9, true)]
    [Arguments(0.5, true)]
    [Arguments(0.4, false)]
    public async Task PassesHybridThreshold_KeepsScoresAtOrAboveThreshold(double score, bool expected) =>
        await Assert.That(DuckDBScoreConverter.PassesHybridThreshold(score, 0.5)).IsEqualTo(expected);

    // ============================================================================================================
    // Item 1 — Native multi-row batch upsert.
    // ============================================================================================================

    // A 700-record batch (crossing the 500-row chunk boundary, so two chunked multi-row MERGE statements) round-trips
    // every record. The elapsed time is reported, not asserted.
    [Test]
    [LanceRequired]
    public async Task BatchUpsert_700Records_ChunksAndRoundTrips() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            const int Count   = 700;
            var       records = new List<DocRecord>(Count);

            for (var i = 0; i < Count; i++) {
                records.Add(
                    new() {
                        Id       = $"r{i}",
                        Category = i % 2 == 0 ? "even" : "odd",
                        Tags     = [$"t{i}"],
                        Content  = $"content {i}",
                        Vec      = new([i, i + 1, i + 2, i + 3])
                    });
            }

            var stopwatch = Stopwatch.StartNew();
            await collection.UpsertAsync(records);
            stopwatch.Stop();

            ReportTiming(
                $"BatchUpsert 700 records (index present, chunked): {stopwatch.ElapsedMilliseconds} ms "
              + "(old serial per-record loop measured minutes at this scale)");

            var keys    = records.Select(r => r.Id).ToList();
            var fetched = new List<DocRecord>();

            await foreach (var record in collection.GetAsync(keys, new() { IncludeVectors = true }))
                fetched.Add(record);

            await Assert.That(fetched.Count).IsEqualTo(Count);

            var byId = fetched.ToDictionary(keySelector: r => r.Id, StringComparer.Ordinal);
            await Assert.That(byId.ContainsKey("r0")).IsTrue();
            await Assert.That(byId.ContainsKey("r499")).IsTrue(); // last row of the first chunk
            await Assert.That(byId.ContainsKey("r500")).IsTrue(); // first row of the second chunk
            await Assert.That(byId.ContainsKey("r699")).IsTrue();
            await Assert.That(byId["r500"].Content).IsEqualTo("content 500");
            await Assert.That(byId["r699"].Vec.ToArray()).IsEquivalentTo(new float[] { 699, 700, 701, 702 });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // A batch that repeats a key must not double-insert (DuckDB MERGE would otherwise take WHEN NOT MATCHED for
    // both source rows): the connector de-duplicates client-side, keeping the LAST occurrence.
    [Test]
    [LanceRequired]
    public async Task BatchUpsert_InBatchDuplicateKey_LastOccurrenceWins() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() {
                    Id       = "dup",
                    Category = "c",
                    Tags     = ["a"],
                    Content  = "first",
                    Vec      = new([1f, 0f, 0f, 0f])
                },
                new() {
                    Id       = "other",
                    Category = "c",
                    Tags     = ["b"],
                    Content  = "middle",
                    Vec      = new([0f, 1f, 0f, 0f])
                },
                new() {
                    Id       = "dup",
                    Category = "c",
                    Tags     = ["c"],
                    Content  = "last",
                    Vec      = new([0f, 0f, 1f, 0f])
                }
            ]);

            // Exactly two physical rows exist ("dup" once, "other" once) — no double-insert.
            var fetched = new List<DocRecord>();

            await foreach (var record in collection.GetAsync(["dup", "other"], new() { IncludeVectors = true }))
                fetched.Add(record);

            await Assert.That(fetched.Count).IsEqualTo(2);

            var dup = fetched.Single(r => r.Id == "dup");
            await Assert.That(dup.Content).IsEqualTo("last");
            await Assert.That(dup.Tags).IsEquivalentTo(new List<string> { "c" });
            await Assert.That(dup.Vec.ToArray()).IsEquivalentTo(new[] { 0f, 0f, 1f, 0f });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // ============================================================================================================
    // Item 2 — BTREE index on the key column.
    // ============================================================================================================

    // EnsureCollectionExists creates a BTREE index named "{key}_idx" on the key column. A 550-record upsert against
    // that index is timed and reported (the pre-index baseline measured ~61s for a serial 550-row upsert).
    [Test]
    [LanceRequired]
    public async Task EnsureCollectionExists_CreatesKeyBtreeIndex_AndBatchUpsertIsFast() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            var observed = await ShowIndexesAsync(store, collection.DatasetPath);

            await Assert
                .That(observed.Any(i => i.Name == "id_idx" && IndexTypeMatches(i.Type, "BTREE") && string.Equals(i.Fields, "id", StringComparison.OrdinalIgnoreCase)))
                .IsTrue();

            const int Count   = 550;
            var       records = new List<DocRecord>(Count);

            for (var i = 0; i < Count; i++) {
                records.Add(
                    new() {
                        Id       = $"k{i}",
                        Category = "c",
                        Tags     = ["t"],
                        Content  = $"c{i}",
                        Vec      = new([i, 0f, 0f, 0f])
                    });
            }

            var stopwatch = Stopwatch.StartNew();
            await collection.UpsertAsync(records);
            stopwatch.Stop();

            ReportTiming(
                $"BatchUpsert 550 records with key BTREE index: {stopwatch.ElapsedMilliseconds} ms "
              + "(pre-index serial baseline was ~61000 ms)");

            await Assert.That(await collection.GetAsync("k0")).IsNotNull();
            await Assert.That(await collection.GetAsync("k549")).IsNotNull();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // The already-exists (re-ensure) path also backfills the key index when it is missing.
    [Test]
    [LanceRequired]
    public async Task EnsureCollectionExists_ReEnsure_KeyIndexRemainsPresent() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DocRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            // Re-ensure the same collection: the drift-check path must not throw and the key index stays present.
            var collectionB = store.GetCollection<string, DocRecord>("docs");
            await collectionB.EnsureCollectionExistsAsync();

            var observed = await ShowIndexesAsync(store, collection.DatasetPath);
            await Assert.That(observed.Count(i => i.Name == "id_idx")).IsEqualTo(1);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // ============================================================================================================
    // Item 3 — Reserved-word storage-name rejection.
    // ============================================================================================================

    // A data column whose storage name is a DuckDB reserved word (here "when") is rejected at ensure time, because
    // the provider emits unquoted identifiers and quoting a reserved-word column re-triggers the lance zero-row
    // DELETE bug (probed live for this stage).
    [Test]
    [LanceRequired]
    public async Task EnsureCollectionExists_ReservedWordDataColumn_Throws() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);

            var definition = new VectorStoreCollectionDefinition {
                Properties = [
                    new VectorStoreKeyProperty("Id", typeof(string)) { StorageName                       = "id" },
                    new VectorStoreDataProperty("When", typeof(string)) { StorageName                    = "when" },
                    new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4) { StorageName = "vec" }
                ]
            };

            var collection = store.GetDynamicCollection("docs", definition);

            await Assert
                .That(async () => await collection.EnsureCollectionExistsAsync())
                .Throws<VectorStoreException>()
                .WithMessageContaining("reserved word");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // A reserved-word KEY column is rejected the same way.
    [Test]
    [LanceRequired]
    public async Task EnsureCollectionExists_ReservedWordKeyColumn_Throws() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);

            var definition = new VectorStoreCollectionDefinition {
                Properties = [
                    new VectorStoreKeyProperty("Select", typeof(string)) { StorageName                   = "select" },
                    new VectorStoreDataProperty("Category", typeof(string)) { StorageName                = "category" },
                    new VectorStoreVectorProperty("Vec", typeof(ReadOnlyMemory<float>), 4) { StorageName = "vec" }
                ]
            };

            var collection = store.GetDynamicCollection("docs", definition);

            await Assert
                .That(async () => await collection.EnsureCollectionExistsAsync())
                .Throws<VectorStoreException>()
                .WithMessageContaining("select");
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // ============================================================================================================
    // Item 4 — ScoreThreshold end-to-end.
    // ============================================================================================================

    // A cosine-distance-family search (EuclideanSquaredDistance gives exact brute-force scores 0/2/4 for these
    // vectors): a threshold of 3.0 keeps only the rows scoring <= 3.0, so the third row (score 4) is dropped and
    // the returned count is fewer than Top.
    [Test]
    [LanceRequired]
    public async Task Search_ScoreThreshold_DistanceMetric_DropsRowsAboveThreshold() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, DistanceRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() { Id = "d0", Vec = new([1f, 0f, 0f, 0f]) }, // score 0
                new() { Id = "d1", Vec = new([0f, 1f, 0f, 0f]) }, // score 2
                new() { Id = "d2", Vec = new([-1f, 0f, 0f, 0f]) } // score 4
            ]);

            // Threshold 3.0 keeps {d0, d1}; d2 (score 4) is dropped even though it is within Top=3.
            var kept = await SearchToListAsync(collection.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), 3, new() { ScoreThreshold = 3.0 }));

            await Assert.That(kept.Select(r => r.Record.Id).OrderBy(keySelector: id => id, StringComparer.Ordinal)).IsEquivalentTo(new[] { "d0", "d1" });
            await Assert.That(kept.All(r => r.Score <= 3.0)).IsTrue();

            // A tighter threshold of 1.0 keeps only {d0}.
            var tight = await SearchToListAsync(collection.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), 3, new() { ScoreThreshold = 1.0 }));

            await Assert.That(tight.Select(r => r.Record.Id)).IsEquivalentTo(new[] { "d0" });
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // A similarity-metric search keeps score >= threshold. For cosine similarity and query [1,0,0,0], the aligned
    // vector scores ~1 and the others score <= 0, so a 0.5 threshold keeps only the aligned row.
    [Test]
    [LanceRequired]
    public async Task Search_ScoreThreshold_SimilarityMetric_KeepsScoresAtOrAboveThreshold() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, SimilarityRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() { Id = "s_high", Vec = new([1f, 0f, 0f, 0f]) }, // sim ~1
                new() { Id = "s_zero", Vec = new([0f, 1f, 0f, 0f]) }, // sim ~0
                new() { Id = "s_neg", Vec  = new([-1f, 0f, 0f, 0f]) } // sim ~-1
            ]);

            var kept = await SearchToListAsync(collection.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), 3, new() { ScoreThreshold = 0.5 }));

            await Assert.That(kept.Select(r => r.Record.Id)).IsEquivalentTo(new[] { "s_high" });
            await Assert.That(kept.All(r => r.Score >= 0.5)).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // Hybrid search applies the threshold on the higher-is-better _hybrid_score: an impossibly high threshold drops
    // every result, while an impossibly low one keeps exactly the unfiltered set.
    [Test]
    [LanceRequired]
    public async Task HybridSearch_ScoreThreshold_DropsLowBlendResults() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, HybridRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            await collection.UpsertAsync(
            [
                new() {
                    Id      = "a",
                    Content = "the quick brown fox jumps",
                    Vec     = new([1f, 0f, 0f, 0f])
                },
                new() {
                    Id      = "b",
                    Content = "the lazy dog sleeps",
                    Vec     = new([0f, 1f, 0f, 0f])
                },
                new() {
                    Id      = "c",
                    Content = "quack goes the duck",
                    Vec     = new([-1f, 0f, 0f, 0f])
                }
            ]);

            var unfiltered = await SearchToListAsync(collection.HybridSearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), ["fox"], 3));
            await Assert.That(unfiltered.Count).IsGreaterThanOrEqualTo(1);

            // A threshold above every _hybrid_score drops all results.
            var dropped = await SearchToListAsync(
                collection.HybridSearchAsync(
                    new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), ["fox"], 3,
                    new() { ScoreThreshold = 1_000_000.0 }));

            await Assert.That(dropped.Count).IsEqualTo(0);

            // A threshold below every _hybrid_score keeps the whole unfiltered set.
            var keptAll = await SearchToListAsync(
                collection.HybridSearchAsync(
                    new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), ["fox"], 3,
                    new() { ScoreThreshold = -1_000_000.0 }));

            await Assert.That(keptAll.Count).IsEqualTo(unfiltered.Count);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // ---- Helpers ----

    static async Task<List<VectorSearchResult<T>>> SearchToListAsync<T>(IAsyncEnumerable<VectorSearchResult<T>> source)
        where T : class {
        var results = new List<VectorSearchResult<T>>();

        await foreach (var result in source)
            results.Add(result);

        return results;
    }

    static Task<IReadOnlyList<(string Name, string Type, string Fields, long RowsIndexed)>> ShowIndexesAsync(DuckDBVectorStore store, string datasetPath) =>
        store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SHOW INDEXES ON '{datasetPath.Replace("'", "''", StringComparison.Ordinal)}'";
                using var reader = command.ExecuteReader();
                return DuckDBIndexBuilder.ParseShowIndexes(reader);
            },
            CancellationToken.None);

    /// <summary>Matches a <c>SHOW INDEXES</c> index type against a <c>USING</c> type, ignoring case and underscores.</summary>
    static bool IndexTypeMatches(string observedType, string usingType) =>
        string.Equals(
            observedType.Replace("_", "", StringComparison.Ordinal),
            usingType.Replace("_", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

    static void ReportTiming(string line) {
        Console.WriteLine("[HARDENING-TIMING] " + line);

        lock (s_timingLock) {
            File.AppendAllText(s_timingPath, line + Environment.NewLine);
        }
    }

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-hardening-test-" + Guid.NewGuid().ToString("N"));
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

    // Storage (column) names are deliberately lowercase; see the note in DuckDBCollectionCrudTests about the lance
    // extension's uppercase-column DELETE pushdown bug.
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

    sealed class DistanceRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.EuclideanSquaredDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class SimilarityRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    sealed class HybridRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "content", IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(4, StorageName = "vec", DistanceFunction = DistanceFunction.EuclideanSquaredDistance)]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}