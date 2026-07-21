using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DuckDB.NET.Data;
using DuckLance.Tests.Support;
using Kurrent.Quack;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace DuckLance.Tests.Comparison;

/// <summary>
/// Side-by-side comparison of the two DuckLance connection managers against IDENTICAL seed data in two SEPARATE
/// Lance namespaces: world <c>E</c> ("embedded") drives <see cref="DuckDBConnectionManager"/> — the production,
/// pool-based manager that runs bound-parameter SQL against a local <c>ATTACH</c>ed Lance directory — and world
/// <c>Q</c> ("protocol") drives <see cref="DuckDBQuackConnectionManager"/> — the experimental manager that tunnels
/// fully literal-encoded SQL (<see cref="DuckDBSqlLiteralEncoder.EncodeInto"/>) to a remote-style, in-process
/// <c>quack</c> server. Every statement executed against either world is composed once via the SAME production SQL
/// composer/builder (<see cref="DuckDBSqlComposer"/>, <see cref="DuckDBSearchSqlComposer"/>,
/// <see cref="DuckDBSchemaBuilder"/>, <see cref="DuckDBIndexBuilder"/>, <see cref="DuckDBMaintenanceSql"/>) and then
/// run through each world's own manager, so any divergence in a result is attributable to the manager/protocol, not
/// to a hand-written SQL difference between the two worlds.
/// </summary>
/// <remarks>
/// <para>
/// Both worlds share the canonical <c>vs_docs</c> shape: <c>id</c> (key), <c>category</c>/<c>tags</c>/<c>content</c>
/// (data), <c>vec FLOAT[4]</c> (vector, cosine <c>IVF_FLAT</c>, <c>num_partitions = 1</c> — which makes the index
/// exhaustive rather than approximate, so exact-oracle assertions are safe). World E attaches its temp storage
/// directory under the alias <c>ducklance</c> (table <c>ducklance.main.vs_docs</c>); world Q's in-process server
/// attaches a SEPARATE temp storage directory under the alias <c>ns</c> (table <c>ns.main.vs_docs</c>) and serves it
/// over the loopback <c>quack</c> protocol.
/// </para>
/// <para>
/// <b>Why the unfiltered vector-search oracle (check 2) runs BEFORE the filler rows are inserted:</b> cosine
/// distance is bounded at 2, and the seeded <c>k3</c> row (<c>[-1,0,0,0]</c>, exactly antiparallel to the query
/// <c>[1,0,0,0]</c>) already sits AT that bound — no other vector can score strictly worse than <c>k3</c>. Once ANY
/// filler rows exist, some of them are mathematically guaranteed to be at least as close to the query as <c>k3</c>,
/// which makes the specific identity of an unfiltered top-3's third row implementation-defined. Running the
/// unfiltered oracle while the table holds only the 3 known rows keeps it deterministic; the later filtered checks
/// (3 and 4) use a <c>WHERE</c> predicate that excludes every filler row from their expected result sets by
/// construction, so filler noise cannot affect their correctness.
/// </para>
/// <para>
/// <b>Scope note:</b> per an explicit scope-trim request, this comparison seeds 60 filler rows (not ~200), times
/// each operation over 8 warm iterations after 1 warmup (not 20/3), and OMITS a hybrid-search comparison check
/// (<c>lance_hybrid_search</c>) entirely — ops 1-6 (point select, vector-search oracle, equality filter, containment
/// filter, MERGE upsert insert+update, delete) are covered; hybrid search is not.
/// </para>
/// </remarks>
[LanceRequired]
[NotInParallel]
public class DuckDBManagerComparisonTests {
    const string ServerUri       = "quack:localhost";
    const string Token           = "s3kret-compare-token";
    const string EmbeddedAlias   = "ducklance";
    const string QuackAlias      = "ns";
    const string EmbeddedTable   = "ducklance.main.vs_docs";
    const string QuackTable      = "ns.main.vs_docs";
    const string VectorIndexName = "vec_idx";
    const int    FillerRowCount  = 60;

    // The canonical oracle seed: k1 identical to the query [1,0,0,0], k2 orthogonal, k3 exactly antiparallel.
    // Cosine distances to the query are therefore exactly 0 / 1 / 2.
    static readonly (string Id, string Category, List<string> Tags, string Content, float[] Vec)[] s_oracleRows = [
        ("k1", "cat_a", ["x", "y"], "content k1", [1f, 0f, 0f, 0f]),
        ("k2", "cat_b", ["y", "z"], "content k2", [0f, 1f, 0f, 0f]),
        ("k3", "cat_a", ["z"], "content k3", [-1f, 0f, 0f, 0f])
    ];

    static readonly List<float> s_queryVector = [1f, 0f, 0f, 0f];

    // ---------------------------------------------------------------------------------------------
    // Correctness: comparison checks 1-6, each run against BOTH worlds and asserted against each
    // other AND against the known oracle values.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task EmbeddedVsQuack_ComparisonChecksAgreeWithEachOtherAndTheOracle() {
        var worlds = await CreateWorldsAsync();

        try {
            await SeedOracleRowsAsync(worlds);
            await CreateVectorIndexAsync(worlds);

            // Checks 1 and 2 run while the table holds ONLY the 3 oracle rows -- see the class remarks for why.
            await Check1_PointSelectByKeyAsync(worlds);
            await Check2_VectorSearchOracleAsync(worlds);

            await InsertFillerRowsAsync(worlds);
            await AppendOptimizeVectorIndexAsync(worlds);

            await Check3_EqualityFilteredSearchAsync(worlds);
            await Check4_ContainmentFilteredSearchAsync(worlds);
            await Check5_MergeUpsertInsertThenUpdateAsync(worlds);
            await Check6_DeleteByKeyAsync(worlds);

            const long ExpectedFinalRowCount = 3 + FillerRowCount; // oracle + filler; m1 inserted-then-deleted nets to 0.

            var finalCountE = await CountEmbeddedRowsAsync(worlds.Embedded, EmbeddedTable);
            var finalCountQ = await CountQuackRowsAsync(worlds.Quack, QuackTable);

            await Assert.That(finalCountE).IsEqualTo(ExpectedFinalRowCount);
            await Assert.That(finalCountQ).IsEqualTo(ExpectedFinalRowCount);

            TestContext.Current?.OutputWriter.WriteLine(
                "[DuckDBManagerComparisonTests] Checks 1-6 agreed between the embedded and protocol worlds. "
              + $"Final row count: embedded={finalCountE}, protocol={finalCountQ} (expected {ExpectedFinalRowCount}). "
              + "Hybrid search (op 7) was skipped for time per an explicit scope trim.");
        } finally {
            worlds.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Timings: ops 1 (point select), 2 (vector search), 5 (MERGE upsert). 1 warmup + 8 timed
    // iterations per op per world (scope-trimmed from 3 warmup + 20 timed). Reported only -- no
    // assertion on elapsed time.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task EmbeddedVsQuack_TimingComparison_ReportsMediansForKeyOperations() {
        var worlds = await CreateWorldsAsync();

        try {
            await SeedOracleRowsAsync(worlds);
            await CreateVectorIndexAsync(worlds);
            await InsertFillerRowsAsync(worlds);
            await AppendOptimizeVectorIndexAsync(worlds);

            var selectE = DuckDBSqlComposer.BuildSelectByKeySql(EmbeddedTable, worlds.Model, true);
            var selectQ = DuckDBSqlComposer.BuildSelectByKeySql(QuackTable, worlds.Model, true);

            string[] columns = ["id", "category", "tags", "content"];

            var searchE = DuckDBSearchSqlComposer.BuildVectorSearchSql(
                EmbeddedTable, "vec", 4,
                3, false, columns,
                3, 0);

            var searchQ = DuckDBSearchSqlComposer.BuildVectorSearchSql(
                QuackTable, "vec", 4,
                3, false, columns,
                3, 0);

            var mergeE = DuckDBSqlComposer.BuildMergeUpsertSql(EmbeddedTable, worlds.Model);
            var mergeQ = DuckDBSqlComposer.BuildMergeUpsertSql(QuackTable, worlds.Model);

            var selectTimes = await MeasureAsync(
                embeddedOp: () => QueryEmbeddedAsync(
                    worlds.Embedded, selectE, ["k1"],
                    CancellationToken.None),
                protocolOp: () => worlds.Quack.QueryAsync(selectQ, ["k1"], CancellationToken.None));

            var searchTimes = await MeasureAsync(
                embeddedOp: () => QueryEmbeddedAsync(
                    worlds.Embedded, searchE, [s_queryVector],
                    CancellationToken.None),
                protocolOp: () => worlds.Quack.QueryAsync(searchQ, [s_queryVector], CancellationToken.None));

            var mergeTimes = await MeasureAsync(
                embeddedOp: () => ExecuteEmbeddedNonQueryAsync(
                    worlds.Embedded, mergeE, TimingMergeParameters(),
                    CancellationToken.None),
                protocolOp: () => worlds.Quack.ExecuteNonQueryAsync(mergeQ, TimingMergeParameters(), CancellationToken.None));

            var report = BuildTimingReport(
                ("SelectByKey", selectTimes),
                ("VectorSearch", searchTimes),
                ("MergeUpsert", mergeTimes));

            TestContext.Current?.OutputWriter.WriteLine(report);

            // Timings are reported only, per the task's instructions -- no assertion on elapsed time. The one
            // load-bearing assertion here is that every timed call actually completed without throwing (MeasureAsync
            // would have propagated an exception otherwise); this keeps the timing scenario a real [Test].
            await Assert.That(selectTimes.EmbeddedMedianMs).IsGreaterThanOrEqualTo(0d);
            await Assert.That(searchTimes.EmbeddedMedianMs).IsGreaterThanOrEqualTo(0d);
            await Assert.That(mergeTimes.EmbeddedMedianMs).IsGreaterThanOrEqualTo(0d);
        } finally {
            worlds.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Check 1: point SELECT by key (BuildSelectByKeySql) -- same row, exact vector/tags, both worlds.
    // ---------------------------------------------------------------------------------------------

    static async Task Check1_PointSelectByKeyAsync(Worlds worlds) {
        var sqlE = DuckDBSqlComposer.BuildSelectByKeySql(EmbeddedTable, worlds.Model, true);
        var sqlQ = DuckDBSqlComposer.BuildSelectByKeySql(QuackTable, worlds.Model, true);

        var eRows = await QueryEmbeddedAsync(
            worlds.Embedded, sqlE, ["k1"],
            CancellationToken.None);

        var qRows = await worlds.Quack.QueryAsync(sqlQ, ["k1"], CancellationToken.None);

        await Assert.That(eRows.Count).IsEqualTo(1);
        await Assert.That(qRows.Count).IsEqualTo(1);

        await AssertOracleRowAsync(eRows[0], s_oracleRows[0]);
        await AssertOracleRowAsync(qRows[0], s_oracleRows[0]);
    }

    // ---------------------------------------------------------------------------------------------
    // Check 2: unfiltered vector-search oracle (BuildVectorSearchSql), k=3, query [1,0,0,0] -- both
    // worlds must return k1:0, k2:1, k3:2 in that order. See the class remarks for why this runs
    // before the filler rows exist.
    // ---------------------------------------------------------------------------------------------

    static async Task Check2_VectorSearchOracleAsync(Worlds worlds) {
        string[] columns = ["id", "category", "tags", "content"];

        var sqlE = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            EmbeddedTable, "vec", 4,
            3, false, columns,
            3, 0);

        var sqlQ = DuckDBSearchSqlComposer.BuildVectorSearchSql(
            QuackTable, "vec", 4,
            3, false, columns,
            3, 0);

        var eRows = await QueryEmbeddedAsync(
            worlds.Embedded, sqlE, [s_queryVector],
            CancellationToken.None);

        var qRows = await worlds.Quack.QueryAsync(sqlQ, [s_queryVector], CancellationToken.None);

        await Assert.That(eRows.Count).IsEqualTo(3);
        await Assert.That(qRows.Count).IsEqualTo(3);

        string[] expectedIds       = ["k1", "k2", "k3"];
        double[] expectedDistances = [0d, 1d, 2d];

        for (var i = 0; i < 3; i++) {
            await Assert.That((string)eRows[i]["id"]!).IsEqualTo(expectedIds[i]);
            await Assert.That((string)qRows[i]["id"]!).IsEqualTo(expectedIds[i]);

            var eDist = Convert.ToDouble(eRows[i]["_distance"], CultureInfo.InvariantCulture);
            var qDist = Convert.ToDouble(qRows[i]["_distance"], CultureInfo.InvariantCulture);

            // Embedded path: exact oracle match (validated precedent: DuckDBVectorSearchTests asserts exact 0/1/2
            // for these clean orthogonal/antiparallel unit vectors -- FLOAT32 cosine math introduces no rounding
            // here).
            await Assert.That(eDist).IsEqualTo(expectedDistances[i]);

            // Protocol path: small float tolerance, per the task's explicit allowance -- would be a reportable
            // divergence if it were ever actually needed; in practice it is not (see the final report).
            await Assert.That(Math.Abs(qDist - expectedDistances[i])).IsLessThan(1e-4);

            // Cross-world agreement, directly.
            await Assert.That(Math.Abs(eDist - qDist)).IsLessThan(1e-4);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Check 3: equality-filtered search (BuildFilteredVectorSearchSql, WHERE category = ?) -- same id
    // set in both worlds: {k1, k3} (the two cat_a rows; filler rows use category 'filler').
    // ---------------------------------------------------------------------------------------------

    static async Task Check3_EqualityFilteredSearchAsync(Worlds worlds) {
        var countE = await CountEmbeddedRowsAsync(worlds.Embedded, EmbeddedTable);
        var countQ = await CountQuackRowsAsync(worlds.Quack, QuackTable);

        string[] columns = ["id", "category", "tags", "content"];

        var sqlE = DuckDBSearchSqlComposer.BuildFilteredVectorSearchSql(
            EmbeddedTable, "vec", 4,
            (int)countE, false, columns,
            "category = ?", 10, 0);

        var sqlQ = DuckDBSearchSqlComposer.BuildFilteredVectorSearchSql(
            QuackTable, "vec", 4,
            (int)countQ, false, columns,
            "category = ?", 10, 0);

        var eRows = await QueryEmbeddedAsync(
            worlds.Embedded, sqlE, [s_queryVector, "cat_a"],
            CancellationToken.None);

        var qRows = await worlds.Quack.QueryAsync(sqlQ, [s_queryVector, "cat_a"], CancellationToken.None);

        List<string> eIds = [.. eRows.Select(r => (string)r["id"]!).OrderBy(keySelector: x => x, StringComparer.Ordinal)];
        List<string> qIds = [.. qRows.Select(r => (string)r["id"]!).OrderBy(keySelector: x => x, StringComparer.Ordinal)];

        await Assert.That(eIds).IsEquivalentTo(new[] { "k1", "k3" });
        await Assert.That(qIds).IsEquivalentTo(new[] { "k1", "k3" });
        await Assert.That(eIds).IsEquivalentTo(qIds);
    }

    // ---------------------------------------------------------------------------------------------
    // Check 4: tag-containment oversample (BuildFilteredVectorSearchSql, k=rowcount, WHERE
    // array_has_any(tags, [?]), LIMIT 5) -- same id set in both worlds: {k2, k3} (the two rows tagged
    // 'z'; filler rows are tagged 'none').
    // ---------------------------------------------------------------------------------------------

    static async Task Check4_ContainmentFilteredSearchAsync(Worlds worlds) {
        var countE = await CountEmbeddedRowsAsync(worlds.Embedded, EmbeddedTable);
        var countQ = await CountQuackRowsAsync(worlds.Quack, QuackTable);

        string[] columns = ["id", "category", "tags", "content"];

        var sqlE = DuckDBSearchSqlComposer.BuildFilteredVectorSearchSql(
            EmbeddedTable, "vec", 4,
            (int)countE, false, columns,
            "array_has_any(tags, [?])", 5, 0);

        var sqlQ = DuckDBSearchSqlComposer.BuildFilteredVectorSearchSql(
            QuackTable, "vec", 4,
            (int)countQ, false, columns,
            "array_has_any(tags, [?])", 5, 0);

        var eRows = await QueryEmbeddedAsync(
            worlds.Embedded, sqlE, [s_queryVector, "z"],
            CancellationToken.None);

        var qRows = await worlds.Quack.QueryAsync(sqlQ, [s_queryVector, "z"], CancellationToken.None);

        List<string> eIds = [.. eRows.Select(r => (string)r["id"]!).OrderBy(keySelector: x => x, StringComparer.Ordinal)];
        List<string> qIds = [.. qRows.Select(r => (string)r["id"]!).OrderBy(keySelector: x => x, StringComparer.Ordinal)];

        await Assert.That(eIds).IsEquivalentTo(new[] { "k2", "k3" });
        await Assert.That(qIds).IsEquivalentTo(new[] { "k2", "k3" });
        await Assert.That(eIds).IsEquivalentTo(qIds);
    }

    // ---------------------------------------------------------------------------------------------
    // Check 5: MERGE upsert (BuildMergeUpsertSql) of a new row, re-SELECT -> present and identical in
    // both worlds; then MERGE-update the same key with a changed category -> both reflect it.
    // ---------------------------------------------------------------------------------------------

    static async Task Check5_MergeUpsertInsertThenUpdateAsync(Worlds worlds) {
        var mergeE  = DuckDBSqlComposer.BuildMergeUpsertSql(EmbeddedTable, worlds.Model);
        var mergeQ  = DuckDBSqlComposer.BuildMergeUpsertSql(QuackTable, worlds.Model);
        var selectE = DuckDBSqlComposer.BuildSelectByKeySql(EmbeddedTable, worlds.Model, true);
        var selectQ = DuckDBSqlComposer.BuildSelectByKeySql(QuackTable, worlds.Model, true);

        object?[] insertParams = [
            "m1",
            "cat_new",
            new List<string> { "m" },
            "merged row",
            new List<float> {
                0f,
                0f,
                1f,
                0f
            }
        ];

        await ExecuteEmbeddedNonQueryAsync(
            worlds.Embedded, mergeE, insertParams,
            CancellationToken.None);

        await worlds.Quack.ExecuteNonQueryAsync(mergeQ, insertParams, CancellationToken.None);

        var eAfterInsert = await QueryEmbeddedAsync(
            worlds.Embedded, selectE, ["m1"],
            CancellationToken.None);

        var qAfterInsert = await worlds.Quack.QueryAsync(selectQ, ["m1"], CancellationToken.None);

        await Assert.That(eAfterInsert.Count).IsEqualTo(1);
        await Assert.That(qAfterInsert.Count).IsEqualTo(1);
        await Assert.That((string)eAfterInsert[0]["category"]!).IsEqualTo("cat_new");
        await Assert.That((string)qAfterInsert[0]["category"]!).IsEqualTo("cat_new");
        await Assert.That(ToFloatList(eAfterInsert[0]["vec"]).ToArray()).IsEquivalentTo(new[] { 0f, 0f, 1f, 0f });
        await Assert.That(ToFloatList(qAfterInsert[0]["vec"]).ToArray()).IsEquivalentTo(new[] { 0f, 0f, 1f, 0f });

        object?[] updateParams = [
            "m1",
            "cat_changed",
            new List<string> { "m" },
            "merged row",
            new List<float> {
                0f,
                0f,
                1f,
                0f
            }
        ];

        await ExecuteEmbeddedNonQueryAsync(
            worlds.Embedded, mergeE, updateParams,
            CancellationToken.None);

        await worlds.Quack.ExecuteNonQueryAsync(mergeQ, updateParams, CancellationToken.None);

        var eAfterUpdate = await QueryEmbeddedAsync(
            worlds.Embedded, selectE, ["m1"],
            CancellationToken.None);

        var qAfterUpdate = await worlds.Quack.QueryAsync(selectQ, ["m1"], CancellationToken.None);

        await Assert.That(eAfterUpdate.Count).IsEqualTo(1);
        await Assert.That(qAfterUpdate.Count).IsEqualTo(1);
        await Assert.That((string)eAfterUpdate[0]["category"]!).IsEqualTo("cat_changed");
        await Assert.That((string)qAfterUpdate[0]["category"]!).IsEqualTo("cat_changed");
    }

    // ---------------------------------------------------------------------------------------------
    // Check 6: DELETE by key (BuildDeleteByKeySql) -- gone in both worlds.
    // ---------------------------------------------------------------------------------------------

    static async Task Check6_DeleteByKeyAsync(Worlds worlds) {
        var deleteE = DuckDBSqlComposer.BuildDeleteByKeySql(EmbeddedTable, worlds.Model);
        var deleteQ = DuckDBSqlComposer.BuildDeleteByKeySql(QuackTable, worlds.Model);
        var selectE = DuckDBSqlComposer.BuildSelectByKeySql(EmbeddedTable, worlds.Model, false);
        var selectQ = DuckDBSqlComposer.BuildSelectByKeySql(QuackTable, worlds.Model, false);

        await ExecuteEmbeddedNonQueryAsync(
            worlds.Embedded, deleteE, ["m1"],
            CancellationToken.None);

        await worlds.Quack.ExecuteNonQueryAsync(deleteQ, ["m1"], CancellationToken.None);

        var eRows = await QueryEmbeddedAsync(
            worlds.Embedded, selectE, ["m1"],
            CancellationToken.None);

        var qRows = await worlds.Quack.QueryAsync(selectQ, ["m1"], CancellationToken.None);

        await Assert.That(eRows.Count).IsEqualTo(0);
        await Assert.That(qRows.Count).IsEqualTo(0);
    }

    static async Task AssertOracleRowAsync(
        Dictionary<string, object?> row,
        (string Id, string Category, List<string> Tags, string Content, float[] Vec) expected
    ) {
        await Assert.That((string)row["id"]!).IsEqualTo(expected.Id);
        await Assert.That((string)row["category"]!).IsEqualTo(expected.Category);
        await Assert.That(ToStringList(row["tags"])).IsEquivalentTo(expected.Tags);
        await Assert.That((string)row["content"]!).IsEqualTo(expected.Content);
        await Assert.That(ToFloatList(row["vec"]).ToArray()).IsEquivalentTo(expected.Vec);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixture setup helpers.
    // ---------------------------------------------------------------------------------------------

    static async Task<Worlds> CreateWorldsAsync() {
        var dirE = CreateTempStorageDir("embedded");
        var dirQ = CreateTempStorageDir("quack");

        var model        = BuildCanonicalModel();
        var datasetPathE = Path.Combine(dirE, "vs_docs.lance");
        var datasetPathQ = Path.Combine(dirQ, "vs_docs.lance");

        var embedded = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dirE, "duck.db") }, EmbeddedAlias);

        DuckDBConnection?             server = null;
        DuckDBQuackConnectionManager? quack  = null;

        try {
            server = new("DataSource=:memory:");
            await server.OpenAsync();

            ExecOnConnection(
                server,
                $"INSTALL lance; LOAD lance; INSTALL quack; LOAD quack; ATTACH '{EscapeSql(dirQ)}' AS {QuackAlias} (TYPE LANCE);");

            await ServeAsync(server);

            quack = new(ServerUri, Token);

            // Table creation goes through each world's own manager, exactly like every other statement in this
            // comparison: world E via DuckDBConnectionManager.ExecuteAsync, world Q via a tunneled, literal-encoded
            // quack_query round-trip (DDL is documented as working over the tunnel: it executes on the server and
            // reports 0 affected rows).
            await embedded.ExecuteAsync(
                operation: connection => ExecOnAdvanced(connection, DuckDBSchemaBuilder.BuildCreateTableSql(EmbeddedTable, model)),
                CancellationToken.None);

            await quack.ExecuteNonQueryAsync(DuckDBSchemaBuilder.BuildCreateTableSql(QuackTable, model), null, CancellationToken.None);

            return new(
                embedded, dirE, datasetPathE,
                quack, server, dirQ,
                datasetPathQ, model);
        } catch {
            quack?.Dispose();
            server?.Dispose();
            embedded.Dispose();
            TryDeleteDir(dirE);
            TryDeleteDir(dirQ);
            throw;
        }
    }

    static async Task SeedOracleRowsAsync(Worlds worlds) {
        var mergeManyE = DuckDBSqlComposer.BuildMergeUpsertManySql(EmbeddedTable, worlds.Model, s_oracleRows.Length);
        var mergeManyQ = DuckDBSqlComposer.BuildMergeUpsertManySql(QuackTable, worlds.Model, s_oracleRows.Length);

        var parameters = new List<object?>(s_oracleRows.Length * 5);

        foreach (var (id, category, tags, content, vec) in s_oracleRows) {
            parameters.Add(id);
            parameters.Add(category);
            parameters.Add(tags);
            parameters.Add(content);
            parameters.Add(new List<float>(vec));
        }

        await ExecuteEmbeddedNonQueryAsync(
            worlds.Embedded, mergeManyE, parameters,
            CancellationToken.None);

        await worlds.Quack.ExecuteNonQueryAsync(mergeManyQ, parameters, CancellationToken.None);
    }

    static async Task CreateVectorIndexAsync(Worlds worlds) {
        var vectorProperty = worlds.Model.VectorProperties[0];
        var indexSqlE      = DuckDBIndexBuilder.BuildVectorIndexSql(worlds.EmbeddedDatasetPath, vectorProperty)!;
        var indexSqlQ      = DuckDBIndexBuilder.BuildVectorIndexSql(worlds.QuackDatasetPath, vectorProperty)!;

        await ExecuteEmbeddedNonQueryAsync(
            worlds.Embedded, indexSqlE, null,
            CancellationToken.None);

        await worlds.Quack.ExecuteNonQueryAsync(indexSqlQ, null, CancellationToken.None);
    }

    static async Task InsertFillerRowsAsync(Worlds worlds) {
        await ExecuteEmbeddedNonQueryAsync(
            worlds.Embedded, BuildFillerInsertSql(EmbeddedTable), null,
            CancellationToken.None);

        await worlds.Quack.ExecuteNonQueryAsync(BuildFillerInsertSql(QuackTable), null, CancellationToken.None);
    }

    /// <summary>
    /// Runs the vector index's append-optimize statement in both worlds.
    /// </summary>
    /// <remarks>
    /// PROTOCOL DIVERGENCE (discovered live, not hypothetical): <c>ALTER INDEX ... OPTIMIZE WITH (mode = 'append')</c>
    /// tunneled through <see cref="DuckDBQuackConnectionManager.ExecuteNonQueryAsync"/> throws a
    /// <see cref="FormatException"/> -- "The input string 'optimize_index' was not in a correct format." --
    /// because <see cref="DuckDBQuackConnectionManager"/>'s affected-row reader
    /// (<c>ReadAffectedCount</c>) unconditionally <c>Convert.ToInt64</c>s the first cell of the first returned row,
    /// an assumption that holds for INSERT/UPDATE/DELETE/MERGE (a numeric count) and for CREATE/DROP (an empty
    /// result, defaulting to 0) but NOT for <c>ALTER INDEX ... OPTIMIZE</c>, which returns a non-empty result whose
    /// first column is the STRING status <c>"optimize_index"</c>, not a count. The embedded path has no equivalent
    /// failure (<c>DuckDBConnectionManager.ExecuteAsync</c> just runs <c>ExecuteNonQuery()</c> and ignores whatever
    /// DuckDB.NET reports). Worked around here by routing this one statement through <c>QueryAsync</c> (which reads
    /// the result generically via <c>ReadAllRows</c>) instead of <c>ExecuteNonQueryAsync</c> on the protocol side --
    /// this is a test-side workaround only, not a fix to the production manager.
    /// </remarks>
    static async Task AppendOptimizeVectorIndexAsync(Worlds worlds) {
        var optimizeSql  = DuckDBMaintenanceSql.BuildAppendOptimizeSql(VectorIndexName, worlds.EmbeddedDatasetPath);
        var optimizeSqlQ = DuckDBMaintenanceSql.BuildAppendOptimizeSql(VectorIndexName, worlds.QuackDatasetPath);

        await ExecuteEmbeddedNonQueryAsync(
            worlds.Embedded, optimizeSql, null,
            CancellationToken.None);

        await worlds.Quack.QueryAsync(optimizeSqlQ, null, CancellationToken.None);
    }

    static string BuildFillerInsertSql(string table) =>
        $"INSERT INTO {table} "
      + "SELECT 'f' || lpad(CAST(i AS VARCHAR), 4, '0'), 'filler', ['none']::VARCHAR[], 'filler content ' || i, "
      + "[0.1, 0.2, 0.3, 0.4]::FLOAT[4] "
      + $"FROM range({FillerRowCount}) t(i)";

    static object?[] TimingMergeParameters() => [
        "timing1",
        "cat_timing",
        new List<string> { "t" },
        "timing row",
        new List<float> {
            0.5f,
            0.5f,
            0.5f,
            0.5f
        }
    ];

    static CollectionModel BuildCanonicalModel() =>
        new PermissiveModelBuilder().BuildDynamic(
            new() {
                Properties = [
                    new VectorStoreKeyProperty("id", typeof(string)),
                    new VectorStoreDataProperty("category", typeof(string)),
                    new VectorStoreDataProperty("tags", typeof(List<string>)),
                    new VectorStoreDataProperty("content", typeof(string)),
                    new VectorStoreVectorProperty("vec", typeof(ReadOnlyMemory<float>), 4) { IndexKind = IndexKind.IvfFlat, DistanceFunction = DistanceFunction.CosineDistance }
                ]
            },
            null);

    // ---------------------------------------------------------------------------------------------
    // World-uniform query/exec helpers: the embedded helpers below return/accept the SAME shapes as
    // DuckDBQuackConnectionManager's own QueryAsync/ExecuteNonQueryAsync, so every check above can
    // treat both worlds identically once a manager call returns.
    // ---------------------------------------------------------------------------------------------

    static Task<List<Dictionary<string, object?>>> QueryEmbeddedAsync(
        DuckDBConnectionManager manager, string sql, IReadOnlyList<object?>? parameters, CancellationToken cancellationToken
    ) =>
        manager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                AddParameters(command, parameters);

                var       rows   = new List<Dictionary<string, object?>>();
                using var reader = command.ExecuteReader();

                while (reader.Read()) {
                    var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);

                    for (var i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                    rows.Add(row);
                }

                return rows;
            },
            cancellationToken);

    static Task ExecuteEmbeddedNonQueryAsync(DuckDBConnectionManager manager, string sql, IReadOnlyList<object?>? parameters, CancellationToken cancellationToken) =>
        manager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                AddParameters(command, parameters);
                command.ExecuteNonQuery();
            },
            cancellationToken);

    static void AddParameters(DbCommand command, IReadOnlyList<object?>? parameters) {
        if (parameters is null)
            return;

        foreach (var parameter in parameters)
            command.Parameters.Add(new DuckDBParameter(parameter ?? DBNull.Value));
    }

    static async Task<long> CountEmbeddedRowsAsync(DuckDBConnectionManager manager, string table) {
        var rows = await QueryEmbeddedAsync(
            manager, $"SELECT count(*) AS n FROM {table}", null,
            CancellationToken.None);

        return Convert.ToInt64(rows[0]["n"], CultureInfo.InvariantCulture);
    }

    static async Task<long> CountQuackRowsAsync(DuckDBQuackConnectionManager manager, string table) {
        var rows = await manager.QueryAsync($"SELECT count(*) AS n FROM {table}", null, CancellationToken.None);
        return Convert.ToInt64(rows[0]["n"], CultureInfo.InvariantCulture);
    }

    static void ExecOnConnection(DuckDBConnection connection, string sql) {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static void ExecOnAdvanced(DuckDBAdvancedConnection connection, string sql) {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Starts the in-process quack server, retrying briefly if the loopback port has not yet been released by a prior test.</summary>
    static async Task ServeAsync(DuckDBConnection server) {
        for (var attempt = 0;; attempt++) {
            try {
                using DbCommand command = server.CreateCommand();
                command.CommandText = $"SELECT listen_url FROM quack_serve('{EscapeSql(ServerUri)}', token := '{EscapeSql(Token)}')";
                using var reader = command.ExecuteReader();
                reader.Read();
                return;
            } catch (DbException) when (attempt < 25) {
                await Task.Delay(200);
            }
        }
    }

    static List<float> ToFloatList(object? value) =>
        value switch {
            List<float> list            => list,
            float[] array               => [.. array],
            IEnumerable<float> sequence => [.. sequence],
            _                           => throw new InvalidOperationException($"Unexpected vector value type '{value?.GetType().FullName ?? "null"}'.")
        };

    static List<string> ToStringList(object? value) =>
        value switch {
            List<string> list            => list,
            string[] array               => [.. array],
            IEnumerable<string> sequence => [.. sequence],
            _                            => throw new InvalidOperationException($"Unexpected tags value type '{value?.GetType().FullName ?? "null"}'.")
        };

    static async Task<TimingResult> MeasureAsync(Func<Task> embeddedOp, Func<Task> protocolOp) {
        // Scope-trimmed from 3 warmup / 20 timed to 1 warmup / 8 timed.
        const int WarmupIterations = 1;
        const int TimedIterations  = 8;

        for (var i = 0; i < WarmupIterations; i++) {
            await embeddedOp();
            await protocolOp();
        }

        var embeddedSamples = new List<double>(TimedIterations);

        for (var i = 0; i < TimedIterations; i++) {
            var stopwatch = Stopwatch.StartNew();
            await embeddedOp();
            stopwatch.Stop();
            embeddedSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var protocolSamples = new List<double>(TimedIterations);

        for (var i = 0; i < TimedIterations; i++) {
            var stopwatch = Stopwatch.StartNew();
            await protocolOp();
            stopwatch.Stop();
            protocolSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return new(Median(embeddedSamples), Median(protocolSamples));
    }

    static double Median(List<double> samples) {
        List<double> sorted = [.. samples];
        sorted.Sort();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2d : sorted[mid];
    }

    static string BuildTimingReport(params (string Op, TimingResult Result)[] rows) {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("[DuckDBManagerComparisonTests] timings (N=8 warm iterations, 1 warmup; median ms):");
        builder.AppendLine("op            | embedded median (ms) | protocol median (ms) | ratio (protocol/embedded)");
        builder.AppendLine("--------------|-----------------------|-----------------------|---------------------------");

        foreach (var (op, result) in rows) {
            var ratio = result.EmbeddedMedianMs > 0 ? result.ProtocolMedianMs / result.EmbeddedMedianMs : double.NaN;
            builder.AppendLine($"{op,-13} | {result.EmbeddedMedianMs,21:F3} | {result.ProtocolMedianMs,21:F3} | {ratio,25:F2}x");
        }

        return builder.ToString();
    }

    static string CreateTempStorageDir(string suffix) {
        var dir = Path.Combine(Path.GetTempPath(), $"ducklance-compare-{suffix}-" + Guid.NewGuid().ToString("N"));
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

    static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    readonly record struct TimingResult(double EmbeddedMedianMs, double ProtocolMedianMs);

    /// <summary>Owns both worlds' resources and tears them down together: stop the quack server, dispose both managers/connections, delete both temp directories.</summary>
    sealed class Worlds : IDisposable {
        public Worlds(
            DuckDBConnectionManager embedded,
            string embeddedDir,
            string embeddedDatasetPath,
            DuckDBQuackConnectionManager quack,
            DuckDBConnection quackServer,
            string quackDir,
            string quackDatasetPath,
            CollectionModel model
        ) {
            Embedded            = embedded;
            EmbeddedDir         = embeddedDir;
            EmbeddedDatasetPath = embeddedDatasetPath;
            Quack               = quack;
            QuackServer         = quackServer;
            QuackDir            = quackDir;
            QuackDatasetPath    = quackDatasetPath;
            Model               = model;
        }

        public DuckDBConnectionManager Embedded { get; }

        public string EmbeddedDir { get; }

        public string EmbeddedDatasetPath { get; }

        public DuckDBQuackConnectionManager Quack { get; }

        public DuckDBConnection QuackServer { get; }

        public string QuackDir { get; }

        public string QuackDatasetPath { get; }

        public CollectionModel Model { get; }

        public void Dispose() {
            try {
                ExecOnConnection(QuackServer, $"CALL quack_stop('{EscapeSql(ServerUri)}')");
            } catch (DbException) {
                // Best-effort stop; the server dies with the connection anyway.
            }

            Quack.Dispose();
            QuackServer.Dispose();
            Embedded.Dispose();
            TryDeleteDir(EmbeddedDir);
            TryDeleteDir(QuackDir);
        }
    }

    /// <summary>
    /// A <see cref="CollectionModelBuilder"/> that accepts any CLR type for data and vector properties and does not
    /// require a vector property, so this class can construct the canonical model directly without depending on the
    /// real (production) DuckLance model builder's type validation. Mirrors the identically-named helper duplicated
    /// across this test suite (e.g. <c>DuckDBSqlComposerTests</c>, <c>DuckDBIndexingTests</c>).
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