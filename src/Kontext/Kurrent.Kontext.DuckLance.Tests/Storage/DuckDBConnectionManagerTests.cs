using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DuckLance.Tests.Support;
using Kurrent.Quack;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Integration tests for <see cref="DuckDBConnectionManager"/>. Tests that open a real connection are gated by
/// <see cref="LanceRequiredAttribute"/> because they install/load the DuckDB <c>lance</c> extension and attach a
/// real Lance namespace on disk.
/// </summary>
public class DuckDBConnectionManagerTests {
    const string Alias = "vs";

    // --- Constructor validation (no live connection required) ---

    [Test]
    public async Task Constructor_EmptyDatabasePath_ThrowsArgumentException() {
        var options = new DuckDBVectorStoreOptions { DatabasePath = "" };

        await Assert
            .That(() => new DuckDBConnectionManager(options, Alias))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("")]     // empty
    [Arguments("1abc")] // leading digit
    [Arguments("a b")]  // space
    [Arguments("a-b")]  // hyphen
    [Arguments("a.b")]  // period
    [Arguments("a'b")]  // single quote
    [Arguments("a;b")]  // semicolon
    public async Task Constructor_InvalidAlias_ThrowsArgumentException(string alias) {
        var options = new DuckDBVectorStoreOptions { DatabasePath = Path.Combine(Path.GetTempPath(), "duck.db") };

        await Assert
            .That(() => new DuckDBConnectionManager(options, alias))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_DatabaseFileStemEqualsAlias_ThrowsArgumentException() {
        // DuckDB names the database's own catalog after the file name's stem (up to the first '.'), so a stem
        // equal to the ATTACH alias would make ATTACH IF NOT EXISTS silently no-op — the constructor rejects it.
        var options = new DuckDBVectorStoreOptions { DatabasePath = Path.Combine(Path.GetTempPath(), "ldb.ddb") };

        await Assert
            .That(() => new DuckDBConnectionManager(options, "ldb"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_ResolvesDatabasePathToFullPath() {
        var dir = CreateTempStorageDir();

        try {
            using var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            await Assert.That(manager.DatabaseDirectory).IsEqualTo(Path.GetFullPath(dir));
            await Assert.That(manager.StorageAlias).IsEqualTo(Alias);
            await Assert.That(Path.IsPathFullyQualified(manager.DatabasePath)).IsTrue();
            // The database file is the caller-given path, resolved to its full form.
            await Assert.That(manager.DatabasePath).IsEqualTo(Path.Combine(Path.GetFullPath(dir), "duck.db"));
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- Bootstrap ---

    [Test]
    [LanceRequired]
    public async Task ExecuteAsync_CreatesAndQueriesLanceTable() {
        var dir = CreateTempStorageDir();

        try {
            using var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"CREATE TABLE {Alias}.main.t1 (id VARCHAR)";
                    command.ExecuteNonQuery();
                },
                CancellationToken.None);

            var count = await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT count(*) FROM {Alias}.main.t1";
                    return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                },
                CancellationToken.None);

            await Assert.That(count).IsEqualTo(0L);
            await Assert.That(Directory.Exists(Path.Combine(manager.DatabaseDirectory, "t1.lance"))).IsTrue();
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- Pool reuse ---

    [Test]
    [LanceRequired]
    public async Task ExecuteAsync_SequentialCalls_ReuseSamePhysicalConnection() {
        var dir = CreateTempStorageDir();

        try {
            using var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            DuckDBAdvancedConnection? first  = null;
            DuckDBAdvancedConnection? second = null;

            await manager.ExecuteAsync(operation: connection => first  = connection, CancellationToken.None);
            await manager.ExecuteAsync(operation: connection => second = connection, CancellationToken.None);

            await Assert.That(first).IsNotNull();
            await Assert.That(second).IsNotNull();
            await Assert.That(ReferenceEquals(first, second)).IsTrue();
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- Stale-dataset-cache recycle ---

    [Test]
    [LanceRequired]
    public async Task ExecuteAsync_StaleDatasetCacheError_RecyclesConnectionAndRetriesOnce() {
        var dir = CreateTempStorageDir();

        try {
            using var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            DuckDBAdvancedConnection? first  = null;
            DuckDBAdvancedConnection? second = null;
            var                       calls  = 0;

            var result = await manager.ExecuteAsync(
                operation: connection => {
                    calls++;

                    if (calls == 1) {
                        first = connection;
                        throw new InvalidOperationException("IO Error: LanceError(IO): Not found: /tmp/x/data/y.lance");
                    }

                    second = connection;
                    return 42;
                },
                CancellationToken.None);

            await Assert.That(result).IsEqualTo(42);
            await Assert.That(calls).IsEqualTo(2);
            await Assert.That(first).IsNotNull();
            await Assert.That(second).IsNotNull();
            await Assert.That(ReferenceEquals(first, second)).IsFalse();

            // The pool remains healthy after a recycle: a subsequent operation still works.
            var after = await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                },
                CancellationToken.None);

            await Assert.That(after).IsEqualTo(1);
        } finally {
            TryDeleteDir(dir);
        }
    }

    [Test]
    [LanceRequired]
    public async Task ExecuteAsync_NonMatchingException_PropagatesWithoutRetry() {
        var dir = CreateTempStorageDir();

        try {
            using var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            var calls = 0;

            await Assert
                .That(async () => await manager.ExecuteAsync<int>(
                    operation: connection => {
                        calls++;
                        throw new InvalidOperationException("boom");
                    },
                    CancellationToken.None))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("boom");

            await Assert.That(calls).IsEqualTo(1);
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- Cancellation ---

    [Test]
    public async Task ExecuteAsync_PreCancelledToken_DoesNotRunOperation() {
        var dir = CreateTempStorageDir();

        try {
            using var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var calls = 0;

            await Assert
                .That(async () => await manager.ExecuteAsync(operation: connection => calls++, cts.Token))
                .Throws<OperationCanceledException>();

            await Assert.That(calls).IsEqualTo(0);
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- Dispose ---

    [Test]
    [LanceRequired]
    public async Task Dispose_KeepsStableEngineFile() {
        var dir     = CreateTempStorageDir();
        var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

        try {
            var dbPath = manager.DatabasePath;
            await Assert.That(dbPath).IsEqualTo(Path.Combine(Path.GetFullPath(dir), "duck.db"));

            // Open a connection so the stable DuckDB engine file is actually created on disk.
            await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    command.ExecuteScalar();
                },
                CancellationToken.None);

            await Assert.That(File.Exists(dbPath)).IsTrue();

            manager.Dispose();

            // The database file is durable state: it SURVIVES Dispose (checkpointed, never deleted) and stays at
            // the caller-given DatabasePath. Dispose also checkpoints, so the WAL is flushed away.
            await Assert.That(File.Exists(dbPath)).IsTrue();
            await Assert.That(File.Exists(dbPath + ".wal")).IsFalse();
        } finally {
            manager.Dispose();
            TryDeleteDir(dir);
        }
    }

    // --- ExtensionPath (LOAD from a pre-downloaded extension file) ---

    [Test]
    [LanceRequired]
    public async Task ExtensionPath_LoadsLanceFromFile() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var extensionPath = Path.Combine(
            home, ".duckdb", "extensions",
            "v1.5.3", "osx_arm64", "lance.duckdb_extension");

        if (!File.Exists(extensionPath)) {
            TestContext.Current?.OutputWriter.WriteLine($"Skipping ExtensionPath test: extension file not found at {extensionPath}");
            return;
        }

        var dir = CreateTempStorageDir();

        try {
            var       options = new DuckDBVectorStoreOptions { DatabasePath = Path.Combine(dir, "duck.db"), ExtensionPath = extensionPath };
            using var manager = new DuckDBConnectionManager(options, Alias);

            var version = await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT extension_version FROM duckdb_extensions() WHERE extension_name = 'lance'";
                    return command.ExecuteScalar() as string;
                },
                CancellationToken.None);

            TestContext.Current?.OutputWriter.WriteLine($"lance extension_version (LOAD from file): {version}");

            await Assert.That(version).IsNotNull();
            await Assert.That(version!).IsNotEmpty();
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- Stable engine file (durable across store lifetimes) ---

    [Test]
    [LanceRequired]
    public async Task StableEngineFile_SurvivesDispose_AndIsReopenedBySecondManager() {
        var dir = CreateTempStorageDir();

        try {
            var expectedDbPath = Path.Combine(Path.GetFullPath(dir), "duck.db");

            // First manager: create a Lance table, write rows, confirm the engine file materialized, then dispose.
            var first = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            try {
                await Assert.That(first.DatabasePath).IsEqualTo(expectedDbPath);

                await first.ExecuteAsync(
                    operation: connection => {
                        using var command = connection.CreateCommand();

                        command.CommandText =
                            $"CREATE TABLE {Alias}.main.reopen (id VARCHAR); INSERT INTO {Alias}.main.reopen VALUES ('one'),('two')";

                        command.ExecuteNonQuery();
                    },
                    CancellationToken.None);

                await Assert.That(File.Exists(expectedDbPath)).IsTrue();
            } finally {
                first.Dispose();
            }

            // The engine file survives the first manager's disposal (durable state, never deleted).
            await Assert.That(File.Exists(expectedDbPath)).IsTrue();

            // A SECOND manager over the same DatabasePath reopens the very same database file and reads the data
            // the first manager wrote — database-file reuse across store lifetimes.
            using var second = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);
            await Assert.That(second.DatabasePath).IsEqualTo(expectedDbPath);

            var count = await second.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT count(*) FROM {Alias}.main.reopen";
                    return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                },
                CancellationToken.None);

            await Assert.That(count).IsEqualTo(2L);
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- The collision fix: two live connections on ONE manager (shared instance) ---

    [Test]
    [LanceRequired]
    public async Task ConcurrentOperations_SecondConnectionInitialize_DoesNotCollideOnAttach() {
        var dir = CreateTempStorageDir();

        try {
            using var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            // Seed one Lance row up front (opens+attaches the first pooled connection).
            await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();

                    command.CommandText =
                        $"CREATE TABLE {Alias}.main.race (id VARCHAR); INSERT INTO {Alias}.main.race VALUES ('x')";

                    command.ExecuteNonQuery();
                },
                CancellationToken.None);

            // Force two operations to hold their rented connections SIMULTANEOUSLY: one reuses the pooled
            // connection, the other's Rent finds none idle and Opens a fresh physical connection whose Initialize
            // runs ATTACH IF NOT EXISTS while the first connection is live on the shared in-process instance.
            // Under a bare ATTACH that second Initialize would throw "database with name ldb already exists";
            // ATTACH IF NOT EXISTS no-ops. Both operations must therefore complete and observe the same seeded row.
            using var barrier = new Barrier(2);

            Task<long> ReadWithOverlapAsync() =>
                Task.Run(() => manager.ExecuteAsync(
                    operation: connection => {
                        barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                        using var command = connection.CreateCommand();
                        command.CommandText = $"SELECT count(*) FROM {Alias}.main.race";
                        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                    },
                    CancellationToken.None));

            var results = await Task.WhenAll(ReadWithOverlapAsync(), ReadWithOverlapAsync());

            await Assert.That(results[0]).IsEqualTo(1L);
            await Assert.That(results[1]).IsEqualTo(1L);
        } finally {
            TryDeleteDir(dir);
        }
    }

    // --- Two managers over the same DatabasePath in one process (shared instance) ---

    [Test]
    [LanceRequired]
    public async Task TwoManagersOverSameDatabasePath_ShareInstance_BothOperateCorrectly() {
        var                      dir      = CreateTempStorageDir();
        DuckDBConnectionManager? managerA = null;
        DuckDBConnectionManager? managerB = null;

        try {
            managerA = new(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);
            managerB = new(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

            // Both resolve to the same database file.
            await Assert.That(managerA.DatabasePath).IsEqualTo(managerB.DatabasePath);

            // Manager A bootstraps first (its connection performs the real ATTACH). Manager B's own connection,
            // sharing the same in-process DuckDB instance, hits the already-attached case and its
            // ATTACH IF NOT EXISTS no-ops — so B must operate correctly and see A's data.
            await managerA.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();

                    command.CommandText =
                        $"CREATE TABLE {Alias}.main.shared (id VARCHAR); INSERT INTO {Alias}.main.shared VALUES ('a'),('b'),('c')";

                    command.ExecuteNonQuery();
                },
                CancellationToken.None);

            var fromB = await managerB.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT count(*) FROM {Alias}.main.shared";
                    return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                },
                CancellationToken.None);

            await Assert.That(fromB).IsEqualTo(3L);

            // A write through B is visible from A: a single shared instance/catalog.
            await managerB.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"INSERT INTO {Alias}.main.shared VALUES ('d')";
                    command.ExecuteNonQuery();
                },
                CancellationToken.None);

            var fromA = await managerA.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT count(*) FROM {Alias}.main.shared";
                    return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                },
                CancellationToken.None);

            await Assert.That(fromA).IsEqualTo(4L);
        } finally {
            managerA?.Dispose();
            managerB?.Dispose();
            TryDeleteDir(dir);
        }
    }

    // --- Cross-process lock characterization ---

    [Test]
    [LanceRequired]
    public async Task CrossProcessOpen_IsRejectedByDuckDBFileLock() {
        var duckdbCli = FindDuckDbCli();

        if (duckdbCli is null) {
            TestContext.Current?.OutputWriter.WriteLine("Skipping cross-process lock test: 'duckdb' CLI not found on PATH.");
            return;
        }

        var dir     = CreateTempStorageDir();
        var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

        try {
            // Open and hold a live connection so THIS process owns the engine file's READ_WRITE lock.
            await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    command.ExecuteScalar();
                },
                CancellationToken.None);

            await Assert.That(File.Exists(manager.DatabasePath)).IsTrue();

            // A second OS process opening the SAME engine file READ_WRITE must fail fast with DuckDB's lock error.
            using var process = new Process {
                StartInfo = new(duckdbCli, $"\"{manager.DatabasePath}\" -c \"SELECT 1\"") {
                    RedirectStandardError  = true,
                    RedirectStandardOutput = true,
                    UseShellExecute        = false
                }
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            _ = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            TestContext.Current?.OutputWriter.WriteLine($"duckdb CLI exit={process.ExitCode}, stderr: {stderr.Trim()}");

            await Assert.That(process.ExitCode).IsNotEqualTo(0);
            await Assert.That(stderr).Contains("Could not set lock on file");
            await Assert.That(stderr).Contains("Conflicting lock is held");
        } finally {
            manager.Dispose();
            TryDeleteDir(dir);
        }
    }

    // --- MemoryLimitMib maps to DuckDB's memory_limit ---

    [Test]
    [LanceRequired]
    public async Task MemoryLimitMib_IsAppliedToEngineMemoryLimitSetting() {
        var dir = CreateTempStorageDir();

        try {
            var       options = new DuckDBVectorStoreOptions { DatabasePath = Path.Combine(dir, "duck.db"), MemoryLimitMib = 256 };
            using var manager = new DuckDBConnectionManager(options, Alias);

            var memoryLimit = await manager.ExecuteAsync(
                operation: connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT current_setting('memory_limit')";
                    return command.ExecuteScalar() as string;
                },
                CancellationToken.None);

            TestContext.Current?.OutputWriter.WriteLine($"memory_limit current_setting = {memoryLimit}");

            // The configured 256 MiB is reflected in DuckDB's memory_limit setting (DuckDB reports "256.0 MiB").
            await Assert.That(memoryLimit).IsNotNull();
            await Assert.That(memoryLimit!).Contains("256");
        } finally {
            TryDeleteDir(dir);
        }
    }

    /// <summary>Locates the <c>duckdb</c> CLI on PATH, or returns <see langword="null"/> when it is not installed.</summary>
    static string? FindDuckDbCli() {
        var exe     = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "duckdb.exe" : "duckdb";
        var pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (pathEnv is null)
            return null;

        foreach (var entry in pathEnv.Split(Path.PathSeparator)) {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var candidate = Path.Combine(entry.Trim(), exe);

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-test-" + Guid.NewGuid().ToString("N"));
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
}