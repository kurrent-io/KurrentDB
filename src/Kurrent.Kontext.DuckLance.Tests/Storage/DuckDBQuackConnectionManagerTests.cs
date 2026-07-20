using System.Data.Common;
using System.Globalization;
using DuckDB.NET.Data;
using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Integration tests for <see cref="DuckDBQuackConnectionManager"/> against an in-process <c>quack</c> server. Every
/// test spins up a fresh server (lance + quack loaded, a temp Lance namespace attached, the canonical <c>vs_docs</c>
/// seeded with a cosine IVF_FLAT index) on the fixed loopback port and tears it down in a <c>finally</c>. Because the
/// server binds a single well-known port, the whole class is <see cref="NotInParallelAttribute">[NotInParallel]</see>,
/// and every test is <see cref="LanceRequiredAttribute">[LanceRequired]</see> (it needs the lance extension).
/// </summary>
[NotInParallel]
[LanceRequired]
public class DuckDBQuackConnectionManagerTests {
    const string ServerUri = "quack:localhost";
    const string Token     = "s3kret-quack-token";

    // The canonical seed: k1 identical to the query, k2 orthogonal, k3 opposite; cosine distances 0 / 1 / 2.
    static readonly (string Id, float[] Vec, string[] Tags)[] s_seed = [
        ("k1", [1f, 0f, 0f, 0f], ["red", "blue"]), ("k2", [0f, 1f, 0f, 0f], ["green"]), ("k3", [-1f, 0f, 0f, 0f], ["red"])
    ];

    // ---------------------------------------------------------------------------------------------
    // 1. A trivial literal SELECT tunnels through and returns its scalar.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task QueryAsync_SelectLiteral_ReturnsScalar() {
        using var fixture = await QuackServerFixture.StartAsync();
        using var manager = new DuckDBQuackConnectionManager(ServerUri, Token);

        var rows = await manager.QueryAsync("SELECT 42 AS answer", null, CancellationToken.None);

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(Convert.ToInt64(rows[0]["answer"], CultureInfo.InvariantCulture)).IsEqualTo(42L);
    }

    // ---------------------------------------------------------------------------------------------
    // 2. The golden oracle: an ENCODED vector parameter flows encode -> wire -> server-side lance search,
    //    producing the exact cosine distances 0 / 1 / 2 in k1, k2, k3 order.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task QueryAsync_ServerSideLanceSearch_WithEncodedVector_ReturnsOracleDistances() {
        using var fixture = await QuackServerFixture.StartAsync();
        using var manager = new DuckDBQuackConnectionManager(ServerUri, Token);

        // Composer-shaped SQL: the vector is a '?' wrapped in CAST(? AS FLOAT[4]); the encoder fills it in.
        const string Sql =
            "SELECT id, _distance FROM lance_vector_search('ns.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 3, prefilter := true) ORDER BY _distance";

        var rows = await manager.QueryAsync(
            Sql,
            [new[] { 1f, 0f, 0f, 0f }],
            CancellationToken.None);

        await Assert.That(rows.Count).IsEqualTo(3);
        await Assert.That((string)rows[0]["id"]!).IsEqualTo("k1");
        await Assert.That((string)rows[1]["id"]!).IsEqualTo("k2");
        await Assert.That((string)rows[2]["id"]!).IsEqualTo("k3");
        await AssertDistance(rows[0], 0d);
        await AssertDistance(rows[1], 1d);
        await AssertDistance(rows[2], 2d);
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Injection probe: a malicious string parameter is contained by the encoder — the WHERE matches
    //    nothing AND the table it tried to drop is still there afterward.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task QueryAsync_InjectionAttempt_IsContained_TableSurvives() {
        using var fixture = await QuackServerFixture.StartAsync();
        using var manager = new DuckDBQuackConnectionManager(ServerUri, Token);

        var matched = await manager.QueryAsync(
            "SELECT id FROM ns.main.vs_docs WHERE id = ?",
            ["x'; DROP TABLE ns.main.vs_docs; --"],
            CancellationToken.None);

        await Assert.That(matched.Count).IsEqualTo(0);

        // The table is intact: still exactly the three seeded rows.
        var count = await manager.QueryAsync("SELECT count(*) AS n FROM ns.main.vs_docs", null, CancellationToken.None);
        await Assert.That(Convert.ToInt64(count[0]["n"], CultureInfo.InvariantCulture)).IsEqualTo(s_seed.Length);
    }

    // ---------------------------------------------------------------------------------------------
    // 4. Server-side MERGE upsert through ExecuteNonQueryAsync with encoded values; the new row is then
    //    visible via a subsequent QueryAsync.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ExecuteNonQueryAsync_ServerSideMergeUpsert_IsVisibleAfterwards() {
        using var fixture = await QuackServerFixture.StartAsync();
        using var manager = new DuckDBQuackConnectionManager(ServerUri, Token);

        const string Merge =
            "MERGE INTO ns.main.vs_docs AS t "
          + "USING (SELECT ? AS id, CAST(? AS FLOAT[4]) AS vec, CAST(? AS VARCHAR[]) AS tags) AS s "
          + "ON t.id = s.id "
          + "WHEN MATCHED THEN UPDATE SET vec = s.vec, tags = s.tags "
          + "WHEN NOT MATCHED THEN INSERT (id, vec, tags) VALUES (s.id, s.vec, s.tags)";

        var affected = await manager.ExecuteNonQueryAsync(
            Merge,
            ["k4", new[] { 0f, 0f, 1f, 0f }, new List<string> { "new" }],
            CancellationToken.None);

        await Assert.That(affected).IsEqualTo(1L);

        var fetched = await manager.QueryAsync(
            "SELECT id FROM ns.main.vs_docs WHERE id = ?",
            ["k4"],
            CancellationToken.None);

        await Assert.That(fetched.Count).IsEqualTo(1);
        await Assert.That((string)fetched[0]["id"]!).IsEqualTo("k4");
    }

    // ---------------------------------------------------------------------------------------------
    // 5. A wrong token surfaces the server's authentication failure cleanly.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task QueryAsync_WrongToken_SurfacesAuthenticationError() {
        using var fixture    = await QuackServerFixture.StartAsync();
        using var badManager = new DuckDBQuackConnectionManager(ServerUri, "not-the-right-token");

        // The server's auth failure surfaces from the client's quack_query call as a DuckDBException (a DbException).
        DbException? caught = null;

        try {
            await badManager.QueryAsync("SELECT 1 AS v", null, CancellationToken.None);
        } catch (DbException ex) {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.ToString()).Contains("Authentication failed");
    }

    // ---------------------------------------------------------------------------------------------
    // 6. Type fidelity: a List<string> tags column and a List<float> vector column come back exact.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task QueryAsync_TypeFidelity_ListAndVectorRoundTrip() {
        using var fixture = await QuackServerFixture.StartAsync();
        using var manager = new DuckDBQuackConnectionManager(ServerUri, Token);

        var rows = await manager.QueryAsync(
            "SELECT id, tags, vec FROM ns.main.vs_docs WHERE id = ?",
            ["k1"],
            CancellationToken.None);

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That((List<string>)rows[0]["tags"]!).IsEquivalentTo(new List<string> { "red", "blue" });
        await Assert.That(((List<float>)rows[0]["vec"]!).ToArray()).IsEquivalentTo(new[] { 1f, 0f, 0f, 0f });
    }

    // ---------------------------------------------------------------------------------------------
    // Disposed-guard: work after Dispose throws ObjectDisposedException (no server needed).
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task QueryAsync_AfterDispose_ThrowsObjectDisposed() {
        using var fixture = await QuackServerFixture.StartAsync();
        var       manager = new DuckDBQuackConnectionManager(ServerUri, Token);
        manager.Dispose();

        await Assert
            .That(async () => await manager.QueryAsync("SELECT 1 AS v", null, CancellationToken.None))
            .Throws<ObjectDisposedException>();
    }

    static async Task AssertDistance(Dictionary<string, object?> row, double expected) {
        var actual = Convert.ToDouble(row["_distance"], CultureInfo.InvariantCulture);
        await Assert.That(Math.Abs(actual - expected)).IsLessThan(1e-4);
    }

    /// <summary>
    /// An in-process quack server: a live DuckDB connection with <c>lance</c> + <c>quack</c> loaded, a temp Lance
    /// namespace attached as <c>ns</c>, the seeded <c>vs_docs</c> table + cosine index, and <c>quack_serve</c> running
    /// on the loopback port. Disposing it stops the server, closes the connection, and deletes the temp directory.
    /// </summary>
    sealed class QuackServerFixture : IDisposable {
        readonly string           _dir;
        readonly DuckDBConnection _server;

        QuackServerFixture(DuckDBConnection server, string dir) {
            _server = server;
            _dir    = dir;
        }

        public void Dispose() {
            try {
                Exec(_server, $"CALL quack_stop('{Escape(ServerUri)}')");
            } catch (DbException) {
                // Best-effort stop; the server dies with the connection anyway.
            }

            _server.Dispose();
            TryDeleteDir(_dir);
        }

        public static async Task<QuackServerFixture> StartAsync() {
            var dir = Path.Combine(Path.GetTempPath(), "ducklance-quack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            var server = new DuckDBConnection("DataSource=:memory:");

            try {
                await server.OpenAsync();

                var datasetPath = Path.Combine(dir, "vs_docs.lance");
                Exec(server, $"INSTALL lance; LOAD lance; INSTALL quack; LOAD quack; ATTACH '{Escape(dir)}' AS ns (TYPE LANCE);");
                Exec(server, BuildSeedSql());
                Exec(server, $"CREATE INDEX vec_idx ON '{Escape(datasetPath)}' (vec) USING IVF_FLAT WITH (metric_type = 'cosine', num_partitions = 1)");

                await ServeAsync(server);

                return new(server, dir);
            } catch {
                server.Dispose();
                TryDeleteDir(dir);
                throw;
            }
        }

        /// <summary>Starts the server, retrying briefly if the loopback port has not yet been released by a prior test.</summary>
        static async Task ServeAsync(DuckDBConnection server) {
            for (var attempt = 0;; attempt++) {
                try {
                    // quack_serve is a (non-blocking) table function; read its row so the server is definitely started.
                    using DbCommand command = server.CreateCommand();
                    command.CommandText = $"SELECT listen_url FROM quack_serve('{Escape(ServerUri)}', token := '{Escape(Token)}')";
                    using var reader = command.ExecuteReader();
                    reader.Read();
                    return;
                } catch (DbException) when (attempt < 25) {
                    await Task.Delay(200);
                }
            }
        }

        static string BuildSeedSql() {
            var rows = new List<string>(s_seed.Length);

            foreach (var (id, vec, tags) in s_seed) {
                var vecLiteral  = DuckDBSqlLiteralEncoder.Encode(vec);
                var tagsLiteral = DuckDBSqlLiteralEncoder.Encode(tags);
                rows.Add($"('{id}', {vecLiteral}, {tagsLiteral})");
            }

            return "CREATE TABLE ns.main.vs_docs AS SELECT * FROM (VALUES " + string.Join(", ", rows) + ") AS v(id, vec, tags)";
        }

        static void Exec(DuckDBConnection connection, string sql) {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

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
    }
}