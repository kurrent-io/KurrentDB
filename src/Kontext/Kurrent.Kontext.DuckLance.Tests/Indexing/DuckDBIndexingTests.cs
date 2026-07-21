using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.NET.Data;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace DuckLance.Tests.Indexing;

/// <summary>
/// Unit tests for <see cref="DuckDBIndexKindMapper"/>: the pure string-constant mappings from
/// Microsoft.Extensions.VectorData's <see cref="IndexKind"/>/<see cref="DistanceFunction"/> constants onto
/// Lance's <c>USING</c> index type, <c>metric_type</c>, PQ-family membership, and <c>num_sub_vectors</c>.
/// </summary>
public class DuckDBIndexKindMapperTests {
    // --- GetLanceIndexType ---

    [Test]
    public async Task GetLanceIndexType_Null_ReturnsIvfPq() => await Assert.That(DuckDBIndexKindMapper.GetLanceIndexType(null)).IsEqualTo("IVF_PQ");

    [Test]
    [Arguments(IndexKind.Dynamic, "IVF_PQ")]
    [Arguments(IndexKind.QuantizedFlat, "IVF_PQ")]
    [Arguments(IndexKind.IvfFlat, "IVF_FLAT")]
    [Arguments(IndexKind.Hnsw, "IVF_HNSW_PQ")]
    public async Task GetLanceIndexType_KnownKinds_MapsToExpectedType(string indexKind, string expected) =>
        await Assert.That(DuckDBIndexKindMapper.GetLanceIndexType(indexKind)).IsEqualTo(expected);

    [Test]
    public async Task GetLanceIndexType_Flat_ReturnsNull() => await Assert.That(DuckDBIndexKindMapper.GetLanceIndexType(IndexKind.Flat)).IsNull();

    [Test]
    [Arguments(IndexKind.DiskAnn)]
    [Arguments("garbage")]
    public async Task GetLanceIndexType_UnsupportedKinds_ThrowsNotSupportedException(string indexKind) =>
        await Assert
            .That(() => DuckDBIndexKindMapper.GetLanceIndexType(indexKind))
            .Throws<NotSupportedException>()
            .WithMessageContaining(indexKind);

    // --- GetLanceMetric ---

    [Test]
    public async Task GetLanceMetric_Null_ReturnsCosine() => await Assert.That(DuckDBIndexKindMapper.GetLanceMetric(null)).IsEqualTo("cosine");

    [Test]
    [Arguments(DistanceFunction.CosineDistance, "cosine")]
    [Arguments(DistanceFunction.CosineSimilarity, "cosine")]
    [Arguments(DistanceFunction.DotProductSimilarity, "dot")]
    [Arguments(DistanceFunction.EuclideanSquaredDistance, "l2")]
    public async Task GetLanceMetric_KnownFunctions_MapsToExpectedMetric(string distanceFunction, string expected) =>
        await Assert.That(DuckDBIndexKindMapper.GetLanceMetric(distanceFunction)).IsEqualTo(expected);

    [Test]
    [Arguments(DistanceFunction.EuclideanDistance)]
    [Arguments(DistanceFunction.NegativeDotProductSimilarity)]
    [Arguments(DistanceFunction.HammingDistance)]
    [Arguments(DistanceFunction.ManhattanDistance)]
    [Arguments("garbage")]
    public async Task GetLanceMetric_UnsupportedFunctions_ThrowsNotSupportedException(string distanceFunction) =>
        await Assert
            .That(() => DuckDBIndexKindMapper.GetLanceMetric(distanceFunction))
            .Throws<NotSupportedException>()
            .WithMessageContaining(distanceFunction);

    // --- IsPqFamily ---

    [Test]
    public async Task IsPqFamily_Null_IsTrue() => await Assert.That(DuckDBIndexKindMapper.IsPqFamily(null)).IsTrue();

    [Test]
    [Arguments(IndexKind.Dynamic)]
    [Arguments(IndexKind.QuantizedFlat)]
    [Arguments(IndexKind.Hnsw)]
    public async Task IsPqFamily_PqFamilyKinds_IsTrue(string indexKind) => await Assert.That(DuckDBIndexKindMapper.IsPqFamily(indexKind)).IsTrue();

    [Test]
    [Arguments(IndexKind.Flat)]
    [Arguments(IndexKind.IvfFlat)]
    public async Task IsPqFamily_NonPqFamilyKinds_IsFalse(string indexKind) => await Assert.That(DuckDBIndexKindMapper.IsPqFamily(indexKind)).IsFalse();

    // --- GetNumSubVectors ---

    [Test]
    [Arguments(4, 4)]
    [Arguments(6, 6)]
    [Arguments(10, 10)]
    [Arguments(12, 12)]
    [Arguments(17, 1)]
    [Arguments(384, 16)]
    [Arguments(768, 16)]
    [Arguments(1536, 16)]
    public async Task GetNumSubVectors_ReturnsLargestDivisorAtMost16(int dimensions, int expected) =>
        await Assert.That(DuckDBIndexKindMapper.GetNumSubVectors(dimensions)).IsEqualTo(expected);
}

/// <summary>
/// Unit tests for <see cref="DuckDBIndexBuilder"/>: the golden DDL statements composed for vector and scalar
/// property indexes, and the <c>SHOW INDEXES</c> reader-parsing helper. No live DuckDB connection touches the
/// <c>lance</c> extension anywhere in this class (the <c>ParseShowIndexes_...</c> test below uses a
/// plain in-memory DuckDB literal SELECT), so none of these tests need <see cref="LanceRequiredAttribute"/>.
/// </summary>
public class DuckDBIndexBuilderTests {
    const string DatasetPath = "/data/x.lance";

    // --- BuildVectorIndexSql ---

    [Test]
    public async Task BuildVectorIndexSql_Dim4CosineHnsw_ProducesGoldenStatement() {
        var vec = BuildVectorProperty(
            "vec", 4, IndexKind.Hnsw,
            DistanceFunction.CosineDistance);

        var sql = DuckDBIndexBuilder.BuildVectorIndexSql(DatasetPath, vec);

        await Assert
            .That(sql)
            .IsEqualTo(
                "CREATE INDEX vec_idx ON '/data/x.lance' (vec) USING IVF_HNSW_PQ WITH "
              + "(metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 4, num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 100)");
    }

    [Test]
    public async Task BuildVectorIndexSql_IvfFlat_OmitsPqParams() {
        var vec = BuildVectorProperty(
            "vec", 4, IndexKind.IvfFlat,
            DistanceFunction.CosineDistance);

        var sql = DuckDBIndexBuilder.BuildVectorIndexSql(DatasetPath, vec);

        await Assert.That(sql).IsEqualTo("CREATE INDEX vec_idx ON '/data/x.lance' (vec) USING IVF_FLAT WITH (metric_type = 'cosine', num_partitions = 1)");
    }

    [Test]
    public async Task BuildVectorIndexSql_Flat_ReturnsNull() {
        var vec = BuildVectorProperty(
            "vec", 4, IndexKind.Flat,
            DistanceFunction.CosineDistance);

        await Assert.That(DuckDBIndexBuilder.BuildVectorIndexSql(DatasetPath, vec)).IsNull();
    }

    [Test]
    public async Task BuildVectorIndexSql_NullIndexKindAndDistanceFunction_UsesDynamicDefaultAndCosine() {
        var vec = BuildVectorProperty(
            "vec", 768, null,
            null);

        var sql = DuckDBIndexBuilder.BuildVectorIndexSql(DatasetPath, vec);

        await Assert
            .That(sql)
            .IsEqualTo(
                "CREATE INDEX vec_idx ON '/data/x.lance' (vec) USING IVF_PQ WITH "
              + "(metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 16, num_bits = 8)");
    }

    [Test]
    public async Task BuildVectorIndexSql_QuantizedFlat_ProducesIvfPqStatement() {
        var vec = BuildVectorProperty(
            "vec", 12, IndexKind.QuantizedFlat,
            null);

        var sql = DuckDBIndexBuilder.BuildVectorIndexSql(DatasetPath, vec);

        await Assert
            .That(sql)
            .IsEqualTo(
                "CREATE INDEX vec_idx ON '/data/x.lance' (vec) USING IVF_PQ WITH "
              + "(metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 12, num_bits = 8)");
    }

    [Test]
    public async Task BuildVectorIndexSql_PathWithQuote_IsEscaped() {
        var vec = BuildVectorProperty(
            "vec", 4, IndexKind.IvfFlat,
            DistanceFunction.CosineDistance);

        var sql = DuckDBIndexBuilder.BuildVectorIndexSql("/data/o'brien.lance", vec);

        await Assert.That(sql).IsEqualTo("CREATE INDEX vec_idx ON '/data/o''brien.lance' (vec) USING IVF_FLAT WITH (metric_type = 'cosine', num_partitions = 1)");
    }

    // --- BuildScalarIndexSqls ---

    [Test]
    public async Task BuildScalarIndexSqls_IsIndexed_ScalarType_UsesBtree() {
        var property = BuildDataProperty(
            "category", typeof(string), true,
            false);

        var sqls = DuckDBIndexBuilder.BuildScalarIndexSqls(DatasetPath, property).ToList();

        await Assert.That(sqls.Count).IsEqualTo(1);
        await Assert.That(sqls[0]).IsEqualTo("CREATE INDEX category_idx ON '/data/x.lance' (category) USING BTREE");
    }

    [Test]
    [Arguments(typeof(List<string>))]
    [Arguments(typeof(string[]))]
    public async Task BuildScalarIndexSqls_IsIndexed_StringListType_UsesLabelList(Type type) {
        var property = BuildDataProperty(
            "tags", type, true,
            false);

        var sqls = DuckDBIndexBuilder.BuildScalarIndexSqls(DatasetPath, property).ToList();

        await Assert.That(sqls.Count).IsEqualTo(1);
        await Assert.That(sqls[0]).IsEqualTo("CREATE INDEX tags_idx ON '/data/x.lance' (tags) USING LABEL_LIST");
    }

    [Test]
    public async Task BuildScalarIndexSqls_IsFullTextIndexed_UsesInverted() {
        var property = BuildDataProperty(
            "content", typeof(string), false,
            true);

        var sqls = DuckDBIndexBuilder.BuildScalarIndexSqls(DatasetPath, property).ToList();

        await Assert.That(sqls.Count).IsEqualTo(1);
        await Assert.That(sqls[0]).IsEqualTo("CREATE INDEX content_fts_idx ON '/data/x.lance' (content) USING INVERTED");
    }

    [Test]
    public async Task BuildScalarIndexSqls_BothIndexedAndFullText_ReturnsBothStatements() {
        var property = BuildDataProperty(
            "content", typeof(string), true,
            true);

        var sqls = DuckDBIndexBuilder.BuildScalarIndexSqls(DatasetPath, property).ToList();

        await Assert
            .That(sqls)
            .IsEquivalentTo(
                new List<string> {
                    "CREATE INDEX content_fts_idx ON '/data/x.lance' (content) USING INVERTED", "CREATE INDEX content_idx ON '/data/x.lance' (content) USING BTREE"
                });
    }

    [Test]
    public async Task BuildScalarIndexSqls_NeitherIndexedNorFullText_ReturnsNoStatements() {
        var property = BuildDataProperty(
            "plain", typeof(string), false,
            false);

        var sqls = DuckDBIndexBuilder.BuildScalarIndexSqls(DatasetPath, property).ToList();

        await Assert.That(sqls.Count).IsEqualTo(0);
    }

    // --- ParseShowIndexes ---

    [Test]
    public async Task ParseShowIndexes_ParsesTheValidatedFiveColumnShape() {
        await using DuckDBConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();

        await using DbCommand command = connection.CreateCommand();

        command.CommandText =
            "SELECT * FROM (VALUES "
          + "('vec_idx', 'IVF_HNSW_PQ', 'vec', 20, 'details-a'), "
          + "('category_idx', 'BTREE', 'category', NULL, 'details-b')"
          + ") AS t(index_name, index_type, fields, rows_indexed, details)";

        await using var reader = await command.ExecuteReaderAsync();

        var parsed = DuckDBIndexBuilder.ParseShowIndexes(reader);

        await Assert.That(parsed.Count).IsEqualTo(2);

        await Assert.That(parsed[0].Name).IsEqualTo("vec_idx");
        await Assert.That(parsed[0].Type).IsEqualTo("IVF_HNSW_PQ");
        await Assert.That(parsed[0].Fields).IsEqualTo("vec");
        await Assert.That(parsed[0].RowsIndexed).IsEqualTo(20L);

        await Assert.That(parsed[1].Name).IsEqualTo("category_idx");
        await Assert.That(parsed[1].Type).IsEqualTo("BTREE");
        await Assert.That(parsed[1].Fields).IsEqualTo("category");
        await Assert.That(parsed[1].RowsIndexed).IsEqualTo(0L);
    }

    // --- Test fixtures ---

    static VectorPropertyModel BuildVectorProperty(string storageName, int dimensions, string? indexKind, string? distanceFunction) {
        var definition = new VectorStoreCollectionDefinition {
            Properties = [
                new VectorStoreKeyProperty("id", typeof(string)),
                new VectorStoreVectorProperty("vec", typeof(ReadOnlyMemory<float>), dimensions) {
                    StorageName      = storageName,
                    IndexKind        = indexKind,
                    DistanceFunction = distanceFunction
                }
            ]
        };

        var model = new PermissiveModelBuilder().BuildDynamic(definition, null);
        return model.VectorProperties[0];
    }

    static DataPropertyModel BuildDataProperty(string storageName, Type type, bool isIndexed, bool isFullTextIndexed) {
        var definition = new VectorStoreCollectionDefinition {
            Properties = [
                new VectorStoreKeyProperty("id", typeof(string)),
                new VectorStoreDataProperty("data", type) {
                    StorageName       = storageName,
                    IsIndexed         = isIndexed,
                    IsFullTextIndexed = isFullTextIndexed
                }
            ]
        };

        var model = new PermissiveModelBuilder().BuildDynamic(definition, null);
        return model.DataProperties[0];
    }

    /// <summary>
    /// A <see cref="CollectionModelBuilder"/> that accepts any CLR type for data and vector properties and does
    /// not require a vector property, so these tests can construct arbitrary models directly without depending
    /// on the real (production) DuckLance model builder's type validation.
    /// </summary>
    sealed class PermissiveModelBuilder : CollectionModelBuilder {
        public PermissiveModelBuilder()
            : base(new() { RequiresAtLeastOneVector = false, SupportsMultipleVectors = true }) { }

        protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }

        protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }
    }
}

/// <summary>
/// Integration tests for <see cref="DuckDBIndexBuilder"/>: the SQL it composes is executed against a real
/// DuckDB + <c>lance</c> connection and verified via <c>SHOW INDEXES</c>, so this class is gated by
/// <see cref="LanceRequiredAttribute"/>.
/// </summary>
[LanceRequired]
public class DuckDBIndexingIntegrationTests {
    [Test]
    public async Task BuildVectorIndexSql_EachIndexKind_CreatesTheExpectedLanceIndexType() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, IndexingRecord>("docs");
            await collection.EnsureCollectionExistsAsync();

            // IVF_PQ-family index training requires at least 256 rows ("Not enough rows to train PQ. Requires
            // 256 rows but only N available"), which the Hnsw/QuantizedFlat/Dynamic cases below all exercise.
            // Seed 300 rows directly via bulk SQL (bypassing the ORM's per-row MERGE in UpsertAsync) for speed.
            await SeedBulkRowsAsync(store, collection.QualifiedTableName, 300);

            var datasetPath = collection.DatasetPath;

            // Dynamic/null and QuantizedFlat both resolve to IVF_PQ; IvfFlat and Hnsw are distinct families.
            // One vector index at a time on the same "vec" column: create, verify, drop, then move to the next.
            (string? IndexKind, string ExpectedType)[] cases = [
                (null, "IVF_PQ"),
                (Microsoft.Extensions.VectorData.IndexKind.Dynamic, "IVF_PQ"),
                (Microsoft.Extensions.VectorData.IndexKind.QuantizedFlat, "IVF_PQ"),
                (Microsoft.Extensions.VectorData.IndexKind.IvfFlat, "IVF_FLAT"),
                (Microsoft.Extensions.VectorData.IndexKind.Hnsw, "IVF_HNSW_PQ")
            ];

            foreach (var (indexKind, expectedType) in cases) {
                var vectorProperty = BuildVectorProperty(indexKind);

                var sql = DuckDBIndexBuilder.BuildVectorIndexSql(datasetPath, vectorProperty);
                await Assert.That(sql).IsNotNull();

                await store.ConnectionManager.ExecuteAsync(
                    operation: connection => {
                        using DbCommand command = connection.CreateCommand();
                        command.CommandText = sql;
                        command.ExecuteNonQuery();
                    },
                    CancellationToken.None);

                var observed = await ShowIndexesAsync(store, datasetPath);

                TestContext.Current?.OutputWriter.WriteLine(
                    $"SHOW INDEXES after IndexKind='{indexKind ?? "(null)"}': "
                  + string.Join("; ", observed.Select(i => $"{i.Name}/{i.Type}/{i.Fields}/rows={i.RowsIndexed}")));

                await Assert.That(observed.Any(i => i.Name == "vec_idx" && IndexTypeMatches(i.Type, expectedType))).IsTrue();

                await store.ConnectionManager.ExecuteAsync(
                    operation: connection => {
                        using DbCommand command = connection.CreateCommand();
                        command.CommandText = $"DROP INDEX vec_idx ON '{EscapePath(datasetPath)}'";
                        command.ExecuteNonQuery();
                    },
                    CancellationToken.None);
            }
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task BuildScalarIndexSqls_EachScalarProperty_AreCreatedAutomatically() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, IndexingRecord>("docs");
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(CreateRecords(20));

            var datasetPath = collection.DatasetPath;

            // Scalar indexes for properties marked IsIndexed/IsFullTextIndexed are now auto-created by
            // EnsureCollectionExistsAsync. Verify they exist with the expected index types.
            var observed = await ShowIndexesAsync(store, datasetPath);

            TestContext.Current?.OutputWriter.WriteLine("SHOW INDEXES (scalar): " + string.Join("; ", observed.Select(i => $"{i.Name}/{i.Type}/{i.Fields}/rows={i.RowsIndexed}")));

            await Assert.That(observed.Any(i => i.Name == "category_idx"    && IndexTypeMatches(i.Type, "BTREE"))).IsTrue();
            await Assert.That(observed.Any(i => i.Name == "tags_idx"        && IndexTypeMatches(i.Type, "LABEL_LIST"))).IsTrue();
            await Assert.That(observed.Any(i => i.Name == "content_fts_idx" && IndexTypeMatches(i.Type, "INVERTED"))).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static async Task<IReadOnlyList<(string Name, string Type, string Fields, long RowsIndexed)>> ShowIndexesAsync(DuckDBVectorStore store, string datasetPath) =>
        await store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SHOW INDEXES ON '{EscapePath(datasetPath)}'";
                using var reader = command.ExecuteReader();
                return DuckDBIndexBuilder.ParseShowIndexes(reader);
            },
            CancellationToken.None);

    /// <summary>
    /// Bulk-seeds <paramref name="rowCount"/> filler rows directly via SQL, bypassing the ORM's per-row
    /// <c>MERGE</c> in <see cref="DuckDBCollection{TKey, TRecord}.UpsertAsync(System.Collections.Generic.IEnumerable{TRecord}, CancellationToken)"/>
    /// for speed. Column order/types must match <see cref="IndexingRecord"/>'s "docs" table shape exactly:
    /// <c>id VARCHAR, category VARCHAR, tags VARCHAR[], content VARCHAR, vec FLOAT[8]</c>.
    /// </summary>
    static async Task SeedBulkRowsAsync(DuckDBVectorStore store, string qualifiedTableName, int rowCount) =>
        await store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();

                command.CommandText =
                    $"INSERT INTO {qualifiedTableName} "
                  + "SELECT 'r' || i, 'cat', ['t'], 'filler ' || i, "
                  + "[0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8, "
                  + "0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8, 0.1 + random()*0.8]::FLOAT[8] "
                  + $"FROM range({rowCount}) t(i)";

                command.ExecuteNonQuery();
            },
            CancellationToken.None);

    /// <summary>
    /// Compares an observed <c>SHOW INDEXES</c> <c>index_type</c> value against the <c>USING</c> type name used
    /// to create it, ignoring case AND underscores.
    /// </summary>
    /// <remarks>
    /// Empirically, the lance extension's <c>SHOW INDEXES</c> reports <c>index_type</c> in a squashed PascalCase
    /// form with no separators at all (e.g. creating with <c>USING LABEL_LIST</c> is reported back as
    /// <c>LabelList</c>, <c>USING BTREE</c> as <c>BTree</c>, <c>USING INVERTED</c> as <c>Inverted</c>), which is
    /// not just a casing difference from the <c>USING</c> constant -- plain <see cref="StringComparison.OrdinalIgnoreCase"/>
    /// does not bridge the missing underscore in e.g. "LabelList" vs "LABEL_LIST". Stripping underscores from
    /// both sides before an ordinal-ignore-case comparison bridges both differences at once.
    /// </remarks>
    static bool IndexTypeMatches(string observedType, string usingType) =>
        string.Equals(
            observedType.Replace("_", "", StringComparison.Ordinal),
            usingType.Replace("_", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

    static string EscapePath(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    /// <summary>
    /// Builds a standalone <see cref="VectorPropertyModel"/> (not the live collection's own model) so
    /// <see cref="VectorPropertyModel.IndexKind"/> can be varied per test case. <c>StorageName</c> and
    /// <c>Dimensions</c> match <see cref="IndexingRecord.Vec"/>'s real "vec" column exactly, so the SQL this
    /// model produces is valid against the real collection's dataset.
    /// </summary>
    static VectorPropertyModel BuildVectorProperty(string? indexKind) {
        var definition = new VectorStoreCollectionDefinition {
            Properties = [
                new VectorStoreKeyProperty("id", typeof(string)) { StorageName                       = "id" },
                new VectorStoreVectorProperty("vec", typeof(ReadOnlyMemory<float>), 8) { StorageName = "vec", IndexKind = indexKind }
            ]
        };

        var model = new PermissiveModelBuilder().BuildDynamic(definition, null);
        return model.VectorProperties[0];
    }

    static List<IndexingRecord> CreateRecords(int count) {
        var records = new List<IndexingRecord>(count);

        for (var i = 0; i < count; i++) {
            records.Add(
                new() {
                    Id       = $"r{i}",
                    Category = i % 2 == 0 ? "science" : "history",
                    Tags     = [i % 2 == 0 ? "even" : "odd"],
                    Content  = $"document number {i} about vector indexing",
                    Vec      = new(Enumerable.Range(0, 8).Select(j => (float)(i * 8 + j)).ToArray())
                });
        }

        return records;
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-indexing-test-" + Guid.NewGuid().ToString("N"));
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
    // lance extension's broken DELETE predicate pushdown for uppercase column names.
    sealed class IndexingRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags", IsIndexed = true)]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content", IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(8, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    /// <summary>
    /// A <see cref="CollectionModelBuilder"/> that accepts any CLR type for data and vector properties and does
    /// not require a vector property, used only to build the standalone per-case <see cref="VectorPropertyModel"/>
    /// in <see cref="BuildVectorProperty"/>. The real collection (<see cref="IndexingRecord"/>) is built through
    /// the production <see cref="DuckDBVectorStore"/> path, not through this builder.
    /// </summary>
    sealed class PermissiveModelBuilder : CollectionModelBuilder {
        public PermissiveModelBuilder()
            : base(new() { RequiresAtLeastOneVector = false, SupportsMultipleVectors = true }) { }

        protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }

        protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
            supportedTypes = null;
            return true;
        }
    }
}

/// <summary>
/// Research probe: verifies whether DuckDB's <c>lance</c> extension SQL surface supports deferred
/// ("untrained") vector index creation via a <c>train = false</c>-style <c>WITH</c> parameter on a freshly
/// created, EMPTY (0-row) Lance dataset, and whether scalar indexes can be created on an empty dataset. These
/// facts decide whether a later "create the vector index shape up front on an empty collection, backfill
/// training once enough rows land" design is viable, so they are captured as executable assertions rather than
/// only prose.
/// </summary>
/// <remarks>
/// <para>
/// <b>Findings (as run against the lance extension version this project pins; underlying lance/lance-index
/// 6.0.0):</b> Creating an <c>IVF_PQ</c> (PQ-family) vector index on a completely empty (0-row) Lance dataset
/// ALWAYS fails with the exact message <c>Not supported: Creating empty vector indices with train=False is not
/// yet implemented</c> (full text captured verbatim in <see cref="ExpectedEmptyVectorIndexErrorFragment"/>) --
/// regardless of whether the <c>WITH</c> clause specifies <c>train = false</c>, the alternate/misspelled
/// <c>training = false</c>, or no train-related parameter at all. The SQL surface never rejects either key name
/// as an "unknown field" (both are accepted syntactically); they simply have no observable effect on the
/// outcome, because on a 0-row dataset Lance always attempts -- and refuses -- the same "empty index" code path.
/// Scalar indexes (<c>BTREE</c>, <c>LABEL_LIST</c>, <c>INVERTED</c>) are unaffected by this restriction and
/// create successfully on an empty dataset, reporting <c>rows_indexed = 0</c> via <c>SHOW INDEXES</c>.
/// </para>
/// <para>
/// <b>Design consequence:</b> a "create the vector index shape up front, backfill training later" deferred-index
/// design is NOT viable against this lance extension version -- vector index creation must be deferred entirely
/// (by the caller, or DuckLance's own reindex scheduler) until the collection has enough rows to train, exactly
/// as <see cref="DuckDBIndexingIntegrationTests"/> does today. Because <c>train=false</c> never succeeds, probe
/// (d) from the research brief -- insert 300 rows after a deferred index exists, then run <c>ALTER INDEX
/// vec_idx ON '{path}' OPTIMIZE WITH (mode = 'append')</c> and verify <c>rows_indexed</c> grows -- has no
/// deferred index to fold rows into and was intentionally not run; its precondition ("if train=false works") is
/// false.
/// </para>
/// </remarks>
[LanceRequired]
public class DuckDBEmptyDatasetVectorIndexProbeTests {
    /// <summary>
    /// The verbatim Lance error fragment observed for every empty-dataset <c>IVF_PQ</c> creation attempt below,
    /// captured exactly as returned by the extension: "IO Error: Failed to create Lance index (Lance error:
    /// dataset create_index: Not supported: Creating empty vector indices with train=False is not yet
    /// implemented. Index '{name}' for column 'vec' cannot be created without training., ... (code=29))".
    /// </summary>
    const string ExpectedEmptyVectorIndexErrorFragment =
        "Creating empty vector indices with train=False is not yet implemented";

    [Test]
    public async Task CreateIvfPqIndex_OnEmptyDataset_WithTrainFalse_FailsAsNotYetImplemented() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, ProbeRecord>("probe");
            await collection.EnsureCollectionExistsAsync();
            var escapedPath = EscapePath(collection.DatasetPath);

            // (a) train = false: the SQL surface accepts the parameter syntactically, but Lance still refuses to
            // build an index over zero rows.
            await Assert
                .That(async () => await CreateIndexAsync(
                    store,
                    $"CREATE INDEX vec_idx ON '{escapedPath}' (vec) USING IVF_PQ WITH "
                  + "(metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 4, num_bits = 8, train = false)"))
                .Throws<DuckDBException>()
                .WithMessageContaining(ExpectedEmptyVectorIndexErrorFragment);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task CreateIvfPqIndex_OnEmptyDataset_WithTrainingFalse_FailsIdenticallyToTrainFalse() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, ProbeRecord>("probe");
            await collection.EnsureCollectionExistsAsync();
            var escapedPath = EscapePath(collection.DatasetPath);

            // (b) the alternate/misspelled key "training = false": not rejected as an unknown field, but also
            // not honored -- the outcome (and exact message) is identical to (a) and to no parameter at all.
            await Assert
                .That(async () => await CreateIndexAsync(
                    store,
                    $"CREATE INDEX vec_idx2 ON '{escapedPath}' (vec) USING IVF_PQ WITH "
                  + "(metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 4, num_bits = 8, training = false)"))
                .Throws<DuckDBException>()
                .WithMessageContaining(ExpectedEmptyVectorIndexErrorFragment);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task CreateIvfPqIndex_OnEmptyDataset_WithNoTrainParam_FailsIdenticallyToTrainFalse() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            var collection = store.GetCollection<string, ProbeRecord>("probe");
            await collection.EnsureCollectionExistsAsync();
            var escapedPath = EscapePath(collection.DatasetPath);

            // (b) plain creation, no train-related WITH key at all: same failure as (a)/(b) above, confirming
            // the 256-row training-data floor (not the train/training WITH key) is what actually gates this.
            await Assert
                .That(async () => await CreateIndexAsync(
                    store,
                    $"CREATE INDEX vec_idx3 ON '{escapedPath}' (vec) USING IVF_PQ WITH "
                  + "(metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 4, num_bits = 8)"))
                .Throws<DuckDBException>()
                .WithMessageContaining(ExpectedEmptyVectorIndexErrorFragment);
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    [Test]
    public async Task CreateScalarIndexes_OnEmptyDataset_AllSucceedWithZeroRowsIndexed() {
        var                dir   = CreateTempStorageDir();
        DuckDBVectorStore? store = null;

        try {
            store = NewStore(dir);
            // Use ProbeRecordNoIndexes (with no IsIndexed/IsFullTextIndexed marks) so that
            // EnsureCollectionExistsAsync does not auto-create indexes, allowing manual CREATE INDEX
            // to probe scalar index creation on an empty dataset.
            var collection = store.GetCollection<string, ProbeRecordNoIndexes>("probe");
            await collection.EnsureCollectionExistsAsync();
            var datasetPath = collection.DatasetPath;
            var escapedPath = EscapePath(datasetPath);

            // (c) BTREE, LABEL_LIST, and INVERTED are all unaffected by the empty-dataset restriction that
            // blocks PQ-family vector indexes above.
            foreach (var sql in new[] {
                $"CREATE INDEX cat_idx ON '{escapedPath}' (category) USING BTREE",
                $"CREATE INDEX tags_idx ON '{escapedPath}' (tags) USING LABEL_LIST",
                $"CREATE INDEX content_idx ON '{escapedPath}' (content) USING INVERTED"
            })
                await CreateIndexAsync(store, sql);

            var observed = await store.ConnectionManager.ExecuteAsync(
                operation: connection => {
                    using DbCommand command = connection.CreateCommand();
                    command.CommandText = $"SHOW INDEXES ON '{escapedPath}'";
                    using var reader = command.ExecuteReader();
                    return DuckDBIndexBuilder.ParseShowIndexes(reader);
                },
                CancellationToken.None);

            TestContext.Current?.OutputWriter.WriteLine(
                "SHOW INDEXES on empty dataset: " + string.Join("; ", observed.Select(i => $"{i.Name}/{i.Type}/{i.Fields}/rows={i.RowsIndexed}")));

            await Assert.That(observed.Any(i => i.Name == "cat_idx"     && IndexTypeMatches(i.Type, "BTREE")      && i.RowsIndexed == 0L)).IsTrue();
            await Assert.That(observed.Any(i => i.Name == "tags_idx"    && IndexTypeMatches(i.Type, "LABEL_LIST") && i.RowsIndexed == 0L)).IsTrue();
            await Assert.That(observed.Any(i => i.Name == "content_idx" && IndexTypeMatches(i.Type, "INVERTED")   && i.RowsIndexed == 0L)).IsTrue();
        } finally {
            store?.Dispose();
            TryDeleteDir(dir);
        }
    }

    static Task CreateIndexAsync(DuckDBVectorStore store, string sql) =>
        store.ConnectionManager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            },
            CancellationToken.None);

    /// <summary>See <see cref="DuckDBIndexingIntegrationTests.IndexTypeMatches"/> for why this normalizes both case and underscores.</summary>
    static bool IndexTypeMatches(string observedType, string usingType) =>
        string.Equals(
            observedType.Replace("_", "", StringComparison.Ordinal),
            usingType.Replace("_", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

    static string EscapePath(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    static DuckDBVectorStore NewStore(string dir) => new(new() { DatabasePath = Path.Combine(dir, "duck.db") });

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-empty-index-probe-" + Guid.NewGuid().ToString("N"));
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

    sealed class ProbeRecord {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category", IsIndexed = true)]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags", IsIndexed = true)]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content", IsFullTextIndexed = true)]
        public string Content { get; set; } = "";

        [VectorStoreVector(8, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }

    /// <summary>
    /// Variant of <see cref="ProbeRecord"/> with no IsIndexed/IsFullTextIndexed flags, used by
    /// <see cref="CreateScalarIndexes_OnEmptyDataset_AllSucceedWithZeroRowsIndexed"/> to probe manual scalar
    /// index creation on an empty dataset without auto-creation interference.
    /// </summary>
    sealed class ProbeRecordNoIndexes {
        [VectorStoreKey(StorageName = "id")] public string Id { get; set; } = "";

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = "";

        [VectorStoreData(StorageName = "tags")]
        public List<string> Tags { get; set; } = [];

        [VectorStoreData(StorageName = "content")]
        public string Content { get; set; } = "";

        [VectorStoreVector(8, StorageName = "vec")]
        public ReadOnlyMemory<float> Vec { get; set; }
    }
}