using System.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.SemanticKernel.Connectors.DuckLance;

// These probes deliberately leave rented connections un-disposed (to demonstrate pool/leak shapes)
// and catch broad exceptions to characterize failure surfaces; both are intentional for an audit.
#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA1031 // Do not catch general exception types

namespace DuckLance.Tests.Storage;

/// <summary>
/// ADVERSARIAL AUDIT probes for <see cref="DuckDBConnectionManager"/>'s assumptions about
/// <c>Kurrent.Quack</c> (0.0.0-alpha.181, commit 5b8cb1ba7a192b007fab451fb8505decf2d8865c) and the
/// DotNext <c>BoundedObjectPool</c> it is built on. These tests are deterministic and do NOT require
/// the <c>lance</c> extension: they exercise plain DuckDB (default no-op <c>Initialize</c>) or a
/// synthetic throwing <c>Initialize</c>, so they run on every platform where DuckDB core loads.
/// Each test pins a Quack/DotNext behavior the manager relies on, and (where relevant) confirms the
/// manager's compensating guard.
/// </summary>
[Category("Storage")]
public class DuckDBConnectionManagerQuackAuditTests {
    // ---------------------------------------------------------------------------------------------
    // #1 / #7  Capacity bounds IDLE connections only, and the real number is 33 (32 ring + 1 fast).
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask pool_default_capacity_is_33_ring_plus_fast_item() {
        // DotNext BoundedObjectPool.Capacity == RingBuffer.Length + 1; RingBuffer(32) => 32 => 33.
        // The manager comments speak of "capacity 32"; the pool actually retains up to 33 idle conns.
        // This bounds IDLE retention only — there is no outstanding-object accounting.
        using var pool = new DuckDBConnectionPool($"Data Source={NewTempDbPath()}");
        await Assert.That(pool.Capacity).IsEqualTo(33);
    }

    // ---------------------------------------------------------------------------------------------
    // #7  Every simultaneously-outstanding rent MINTS a distinct physical connection (each running
    //     Initialize = LOAD/ATTACH). Capacity caps idle retention, never concurrency. Nested rents
    //     (no concurrency needed) prove the miss-cost pattern deterministically.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask pool_outstanding_rents_each_mint_distinct_connection() {
        const int N        = 5;
        var       db       = NewTempDbPath();
        var       pool     = new DuckDBConnectionPool($"Data Source={db}"); // default Initialize is a no-op
        var       scopes   = new List<IDisposable>();
        var       distinct = new HashSet<DuckDBAdvancedConnection>(ReferenceEqualityComparer.Instance);

        try {
            for (var i = 0; i < N; i++) {
                var scope = pool.Rent(out var connection); // nothing returned yet => TryGet null => Open()
                scopes.Add(scope);
                distinct.Add(connection);
            }

            // All N were outstanding at once, so N distinct connections were minted+initialized.
            await Assert.That(distinct.Count).IsEqualTo(N);
        } finally {
            foreach (var scope in scopes)
                scope.Dispose();

            pool.Dispose();
            TryDeleteFile(db);
            TryDeleteFile(db + ".wal");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // #6  In-memory rejection at the pin; a file/temp-path Data Source is never mistaken for memory.
    //     (Drift note: at Quack HEAD this rejection is GONE — in-memory is supported via a prototype.)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask pool_in_memory_data_source_is_rejected_at_pin() {
        await Assert
            .That(() => new DuckDBConnectionPool("Data Source=:memory:"))
            .Throws<NotSupportedException>();

        await Assert
            .That(() => new DuckDBConnectionPool("Data Source=:memory:?cache=shared"))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async ValueTask pool_file_path_data_source_with_spaces_is_accepted_not_seen_as_in_memory() {
        var dir = Path.Combine(Path.GetTempPath(), "duck lance audit " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "audit space.ddb");

        try {
            // Must NOT throw NotSupportedException: a real path is never ":memory:".
            using var pool = new DuckDBConnectionPool($"Data Source={db}");
            await Assert.That(pool.Capacity).IsEqualTo(33);
        } finally {
            TryDeleteDir(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // #2(a)  A connection rented BEFORE pool dispose and returned AFTER is DISPOSED (not leaked):
    //        Freeze() makes TryReturn return false, and Quack's Scope disposes the connection on false.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask scope_return_after_pool_dispose_disposes_connection_no_leak() {
        var db = NewTempDbPath();

        try {
            var pool  = new DuckDBConnectionPool($"Data Source={db}"); // default Initialize is a no-op
            var scope = pool.Rent(out var connection);
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);

            // Dispose the pool while the connection is outstanding: Freeze + drain idle (this conn is NOT idle).
            pool.Dispose();
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open); // untouched by drain

            // Now return it: TryReturn on a frozen pool returns false, so Scope.Dispose disposes it.
            ((IDisposable)scope).Dispose();
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);
        } finally {
            TryDeleteFile(db);
            TryDeleteFile(db + ".wal");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // #3  Quack's Open() LEAKS the freshly-opened physical connection if Initialize throws. Proof:
    //     the connection captured inside Initialize is still Open after Rent throws, and pool.Dispose
    //     (drain of idle) cannot reclaim a connection that was never returned. The MANAGER compensates
    //     in LanceConnectionPool.Initialize (try/catch -> connection.Dispose() -> rethrow); this test
    //     pins the raw hazard that compensation depends on.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask open_initialize_throws_quack_leaks_opened_connection() {
        var db   = NewTempDbPath();
        var pool = new CapturingThrowingPool($"Data Source={db}");

        try {
            await Assert.That(() => pool.Rent(out _)).Throws<InvalidOperationException>();

            var leaked = pool.Captured;
            await Assert.That(leaked).IsNotNull();
            // Quack Open() is `connection.Open(); Initialize(connection);` with no try/finally, so an
            // Initialize failure leaves the physical connection OPEN and undisposed.
            await Assert.That(leaked!.State).IsEqualTo(ConnectionState.Open);

            // pool.Dispose() only drains IDLE (returned) connections; the leaked one was never returned.
            pool.Dispose();
            await Assert.That(leaked.State).IsEqualTo(ConnectionState.Open); // STILL leaked

            leaked.Dispose(); // manual cleanup so the temp db file becomes deletable
        } finally {
            pool.Dispose();
            TryDeleteFile(db);
            TryDeleteFile(db + ".wal");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // #4 / #1(b)  TryReturn performs NO liveness/dedup check: returning the SAME connection twice
    //             (the latent Scope-struct copy hazard) admits DUPLICATES, and two Rents then hand the
    //             SAME physical connection to two callers -> pool corruption (DuckDBAdvancedConnection
    //             is documented not-thread-safe). The manager AVOIDS this today (it never copies a
    //             Scope); this proves the danger it is avoiding and pins the no-dedup behavior.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask pool_double_return_same_connection_admits_duplicate_two_rents_share_one_connection() {
        var                       db   = NewTempDbPath();
        var                       pool = new DuckDBConnectionPool($"Data Source={db}");
        DuckDBAdvancedConnection? conn = null;

        try {
            var scope = pool.Rent(out conn);
            var copy  = scope;              // struct copy: same _pool + _connection
            ((IDisposable)scope).Dispose(); // TryReturn(conn) -> fastItem
            ((IDisposable)copy).Dispose();  // TryReturn(conn) AGAIN -> ring buffer (no dedup!)

            _ = pool.Rent(out var c1); // fastItem  -> conn
            _ = pool.Rent(out var c2); // ring slot -> conn (same object)

            await Assert.That(ReferenceEquals(c1, c2)).IsTrue();
            await Assert.That(ReferenceEquals(c1, conn)).IsTrue();
        } finally {
            conn?.Dispose();
            pool.Dispose();
            TryDeleteFile(db);
            TryDeleteFile(db + ".wal");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // #2(b)  The manager DOES guard use-after-dispose: ExecuteAsync after Dispose throws
    //        ObjectDisposedException (a synchronous _disposed check), rather than silently renting a
    //        fresh connection and operating against a disposed store on the still-present stable engine
    //        file. (Quack's Rent/Open have no disposed-check of their own — verified via source; the
    //        manager adds the guard.)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask manager_execute_after_dispose_throws_object_disposed_guarded() {
        var dir     = CreateTempStorageDir();
        var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, "vs");

        try {
            manager.Dispose();

            await Assert
                .That(async () => await manager.ExecuteAsync(operation: _ => 0, CancellationToken.None))
                .Throws<ObjectDisposedException>();
        } finally {
            manager.Dispose();
            // The stable engine file (if any) lives inside dir and is removed with it; the connector never
            // deletes it on Dispose.
            TryDeleteDir(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // #2(c)  Double-dispose of the manager is safe: the manager's own Dispose is idempotent via an
    //        early-return _disposed guard (a second call returns before touching the pool), and it never
    //        deletes the stable engine file.
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async ValueTask manager_double_dispose_is_safe() {
        var dir     = CreateTempStorageDir();
        var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, "vs");

        try {
            manager.Dispose();
            // Second dispose must not throw. No op ever opened a connection, so the manager never checkpointed
            // and the stable engine file was never materialized (and Dispose never deletes it either).
            manager.Dispose();
            await Assert.That(File.Exists(manager.DatabasePath)).IsFalse();
        } finally {
            TryDeleteDir(dir);
        }
    }

    static string NewTempDbPath() => Path.Combine(Path.GetTempPath(), "ducklance-audit-" + Guid.NewGuid().ToString("N") + ".ddb");

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-audit-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void TryDeleteFile(string path) {
        try {
            if (File.Exists(path))
                File.Delete(path);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static void TryDeleteDir(string dir) {
        try {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------------------------------------
    // helpers + fixtures
    // ---------------------------------------------------------------------------------------------

    /// <summary>A pool whose <c>Initialize</c> captures the freshly-opened connection then throws,
    /// emulating an ATTACH/LOAD failure so the audit can observe Quack's leak on the failure path.</summary>
    sealed class CapturingThrowingPool : DuckDBConnectionPool<DuckDBAdvancedConnection> {
        public CapturingThrowingPool(string connectionString)
            : base(connectionString) { }

        public DuckDBAdvancedConnection? Captured { get; private set; }

        protected override void Initialize(DuckDBAdvancedConnection connection) {
            Captured = connection;
            throw new InvalidOperationException("simulated Initialize (LOAD/ATTACH) failure");
        }
    }
}