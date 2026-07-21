using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Owns the DuckDB connection pool for a single DuckLance vector store and runs work against
/// connections that have the <c>lance</c> extension loaded and the store's Lance namespace attached.
/// </summary>
sealed class DuckDBConnectionManager : IDisposable {
    // Per-retry backoff for a transient Lance commit conflict: length (2) plus the initial attempt yields
    // three attempts total; the schedule is deterministic — no randomness.
    static readonly int[] CommitConflictBackoffMs = [20, 50];

    // Characters permitted after the first character of an unquoted SQL identifier (^[A-Za-z_][A-Za-z0-9_]*$).
    static readonly SearchValues<char> IdentifierTailChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");

    readonly DuckDBConnectionPool _pool;

    // Set by Dispose; guards ExecuteAsync against renting from a frozen pool (Kurrent.Quack's Rent/Open
    // perform no disposed-check of their own).
    volatile bool _disposed;

    // Set the first time an operation runs (a physical connection was therefore opened). Gates the dispose-time
    // checkpoint so a manager that never opened a connection does not open one just to flush an empty database —
    // which would needlessly create the database file and attempt a lance load on unsupported platforms.
    volatile bool _everExecuted;

    public DuckDBConnectionManager(DuckDBVectorStoreOptions options, string storageAlias) {
        ArgumentException.ThrowIfNullOrEmpty(options.DatabasePath);

        if (!IsValidAlias(storageAlias)) {
            throw new ArgumentException(
                $"The storage alias '{storageAlias}' is not a valid SQL identifier. It must match ^[A-Za-z_][A-Za-z0-9_]*$.",
                nameof(storageAlias));
        }

        StorageAlias = storageAlias;

        // The caller names the database file; everything else follows by convention: the file's directory IS
        // the Lance namespace that gets attached. Kurrent.Quack's pool ctor rejects in-memory data sources, so
        // a real file is required either way.
        DatabasePath      = Path.GetFullPath(options.DatabasePath);
        DatabaseDirectory = Path.GetDirectoryName(DatabasePath)
                         ?? throw new ArgumentException("The database path must point to a file, not a filesystem root.", nameof(options));

        // DuckDB names the database's own catalog after the file name's stem (up to the first '.'). A stem equal
        // to the ATTACH alias makes ATTACH IF NOT EXISTS silently no-op against that catalog — routing writes
        // away from Lance (live-validated silent-data-loss shape) — so such a path is rejected outright.
        var fileName = Path.GetFileName(DatabasePath);
        var dotIndex = fileName.IndexOf('.');
        var stem     = dotIndex < 0 ? fileName : fileName[..dotIndex];

        if (stem.Length == 0 || stem.Equals(storageAlias, StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException(
                $"The database file name '{fileName}' must have a non-empty stem distinct from the attach alias '{storageAlias}' — DuckDB names the database catalog after the stem.",
                nameof(options));
        }

        // ATTACH does not create the directory, and the database file needs it too. This is pure filesystem
        // work (no DB I/O), so the MEVD "construction is I/O-free on the DB side" convention (SqliteVec) still
        // holds — extension-load/attach errors continue to surface on the first operation, not here.
        Directory.CreateDirectory(DatabaseDirectory);

        _pool = new LanceConnectionPool(
            ConnectionString(), DatabaseDirectory, StorageAlias,
            options.ExtensionPath);

        static bool IsValidAlias(string alias) =>
            !string.IsNullOrEmpty(alias)
         && (char.IsAsciiLetter(alias[0]) || alias[0] == '_')
         && !alias.AsSpan(1).ContainsAnyExcept(IdentifierTailChars);

        // Explicit database settings, the KurrentDB idiom: READ_WRITE access plus an optional memory cap. The MiB
        // spelling is deliberate — DuckDB parses "MB" as decimal, which would round-trip fractionally.
        string ConnectionString() =>
            options.MemoryLimitMib is int mib
                ? string.Create(CultureInfo.InvariantCulture, $"Data Source={DatabasePath};access_mode=READ_WRITE;memory_limit={mib}MiB")
                : $"Data Source={DatabasePath};access_mode=READ_WRITE";
    }

    /// <summary>The ATTACH alias under which the Lance namespace is mounted on every pooled connection.</summary>
    public string StorageAlias { get; }

    /// <summary>The database file's directory — doubles as the attached Lance namespace holding every collection's dataset.</summary>
    public string DatabaseDirectory { get; }

    /// <summary>The resolved absolute path of the DuckDB database file: durable, created on first use, never deleted.</summary>
    public string DatabasePath { get; }

    /// <summary>Best-effort checkpoints the database, then disposes the pool. Idempotent; the file survives.</summary>
    [SuppressMessage(
        "Reliability", "CA1031:Do not catch general exception types",
        Justification =
            "The dispose-time checkpoint is strictly best-effort and must not let any failure escape Dispose; the WAL is durable and replayed on next open, so swallowing loses no data.")]
    [SuppressMessage(
        "Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The rented checkpoint connection is owned by the pool and returned via Scope.Dispose() when the using block exits.")]
    public void Dispose() {
        if (_disposed)
            return;

        _disposed = true;

        // KurrentDB's shutdown idiom: flush the WAL into the database file so it is left consistent on disk. Only
        // worth doing if a connection was ever opened (otherwise there is nothing to flush and opening one just
        // to checkpoint would needlessly materialize the database file). Best-effort — failures are swallowed.
        if (_everExecuted) {
            try {
                using (_pool.Rent(out var connection)) {
                    using var command = connection.CreateCommand();
                    command.CommandText = "CHECKPOINT;";
                    command.ExecuteNonQuery();
                }
            } catch {
                // Best-effort WAL flush: the WAL is durable and DuckDB replays it on the next open, so a missed
                // checkpoint costs recovery work, not data.
            }
        }

        // Disposing the pool freezes it and disposes every idle pooled connection, releasing their handles on the
        // database file. The file itself is intentionally left on disk.
        _pool.Dispose();
    }

    /// <summary>Runs a synchronous operation against a pooled connection, inline on the caller's thread.</summary>
    [SuppressMessage(
        "Reliability", "CA1031:Do not catch general exception types",
        Justification =
            "The inline sync-under-async surface must surface the operation's fault through the returned task (as the previous Task.Run shape did) rather than throw synchronously; any exception is re-projected via Task.FromException.")]
    [SuppressMessage(
        "Reliability", "CA2000:Dispose objects before losing scope",
        Justification =
            "The rented connection is owned by the pool: returned via Scope.Dispose() on the normal path; on the stale-handle path it is disposed directly and its Scope is intentionally dropped so the poisoned connection cannot be returned.")]
    public Task<T> ExecuteAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken) {
        // DuckDB has no true async API, so this async surface runs its synchronous work INLINE on the caller's
        // thread and returns an already-completed task (the SqliteVec embedded-provider convention). Measured:
        // point-select cost is dominated by DuckDB/Lance execution (~0.5 ms), against which a Task.Run hop was a
        // microsecond-scale term plus a per-call allocation and thread-pool fan-out under bursts. The caller's
        // token is honored as a cooperative pre-check; faults/cancellation surface through the returned task.
        //
        // Kurrent.Quack's Rent/Open have no disposed-guard of their own (a rent on a frozen, drained pool
        // silently mints a brand-new connection), so the manager guards here; a benign TOCTOU window vs. a
        // concurrent Dispose remains (the recycle path re-checks before its second rent).
        ObjectDisposedException.ThrowIf(_disposed, this);

        try {
            cancellationToken.ThrowIfCancellationRequested();

            // A physical connection is about to be rented (opened if the pool is empty), so the database has been
            // touched: the dispose-time checkpoint is now worth doing. No admission gate, deliberately:
            // concurrency is the caller's to bound (the KurrentDB convention) — on the shared stable-file
            // instance a pool-miss Initialize is nearly free, and a blocking gate would stall caller threads
            // under a sync-under-async surface.
            _everExecuted = true;

            for (var isFreshRetry = false;; isFreshRetry = true) {
                // NEVER copy the Scope: it is a struct whose Dispose calls the pool's duplicate-unchecked
                // TryReturn — disposing two copies would admit the same (not-thread-safe) connection twice.
                var scope        = _pool.Rent(out var connection);
                var returnToPool = true;

                try {
                    for (var attempt = 0;; attempt++) {
                        try {
                            return Task.FromResult(operation(connection));
                        } catch (Exception ex) when (attempt < CommitConflictBackoffMs.Length && IsRetryableCommitConflict(ex)) {
                            // Lance itself instructs the retry; the connection is healthy. Blocks the caller's
                            // thread — expected for an embedded sync-under-async provider.
                            Thread.Sleep(CommitConflictBackoffMs[attempt]);
                        }
                    }
                } catch (Exception ex) when (IsStaleDatasetHandleError(ex)) {
                    // Poisoned connection (dead cached dataset view): never return it to the pool, whichever
                    // attempt this is. Dropping the Scope un-disposed is what keeps it out — Scope.Dispose() is
                    // the only caller of the pool's liveness-unchecked TryReturn (verified against the
                    // Kurrent.Quack 0.0.0-alpha.181 + DotNext 6.3.0 sources; the pool keeps no outstanding
                    // accounting, so a dropped Scope costs nothing).
                    returnToPool = false;
                    connection.Dispose();

                    if (isFreshRetry)
                        throw;

                    // About to mint a fresh connection (whose Initialize re-ATTACHes, opening a current dataset
                    // view): re-check disposal so an operation racing Dispose does not rent from a frozen pool.
                    ObjectDisposedException.ThrowIf(_disposed, this);
                } finally {
                    if (returnToPool)
                        ((IDisposable)scope).Dispose();
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Only the CALLER's token maps to a canceled task; an operation's internal OperationCanceledException
            // (e.g. its own timeout token) is a fault like any other and falls through to Task.FromException.
            return Task.FromCanceled<T>(cancellationToken);
        } catch (Exception ex) {
            return Task.FromException<T>(ex);
        }

        // Lance's optimistic-concurrency rejection ("... Retryable commit conflict for version N ... Please
        // retry."): transient, connection healthy, safe to re-run on the same connection.
        static bool IsRetryableCommitConflict(Exception ex) => ex.ToString().Contains("Retryable commit conflict", StringComparison.Ordinal);

        // A dead cached dataset view, in either live-validated shape: the vacuum/rewrite shape
        // ("LanceError(IO)" + "Not found") or the concurrent-writer shape ("... rowaddr N belongs to
        // non-existent fragment: M ..."). Retrying on the same connection never converges; only a fresh
        // connection (re-ATTACH) does. Ordinal substring over the full exception text incl. inner exceptions.
        static bool IsStaleDatasetHandleError(Exception ex) {
            var text = ex.ToString();

            return (text.Contains("LanceError(IO)", StringComparison.Ordinal)
                 && text.Contains("Not found", StringComparison.Ordinal))
                || text.Contains("belongs to non-existent fragment", StringComparison.Ordinal);
        }
    }

    /// <summary>Runs a synchronous operation with no result against a pooled connection.</summary>
    public Task ExecuteAsync(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken) =>
        ExecuteAsync<object?>(
            operation: connection => {
                operation(connection);
                return null;
            },
            cancellationToken);

    /// <summary>A pool that loads the <c>lance</c> extension and attaches the Lance namespace on every physical connection it opens.</summary>
    sealed class LanceConnectionPool(string connectionString, string storagePath, string storageAlias, string? extensionPath)
        : DuckDBConnectionPool(connectionString) {
        // Paths are user input interpolated into SQL, so single quotes are doubled; the alias is pre-validated as
        // a bare identifier. ATTACH is instance-scoped, not connection-scoped: DuckDB.NET shares one native
        // instance (and catalog) per database-file path, so a bare ATTACH would fail on the second connection
        // with "Failed to attach database: ... already exists" — IF NOT EXISTS makes it idempotent. Never DETACH:
        // another manager on the same path may share this instance. A second OS process opening the same file
        // READ_WRITE is rejected by DuckDB's file lock ("Could not set lock on file ...").
        readonly string _initializeCommandText =
            $"""
             {(extensionPath is null ? "INSTALL lance; LOAD lance;" : $"LOAD '{Escape(extensionPath)}';")}
             ATTACH IF NOT EXISTS '{Escape(storagePath)}' AS {storageAlias} (TYPE LANCE);
             """;

        protected override void Initialize(DuckDBAdvancedConnection connection) {
            try {
                using var command = connection.CreateCommand();
                command.CommandText = _initializeCommandText;
                command.ExecuteNonQuery();
            } catch (DuckDBException ex) when (ex.Message.Contains($"database with name \"{storageAlias}\" already exists", StringComparison.Ordinal)) {
                // Two pool-miss connections can race Initialize on the same shared instance (e.g. a background
                // reindex tick opening a connection while a foreground operation is still mid-bootstrap): ATTACH
                // IF NOT EXISTS is check-then-create, not atomic, so the race loser throws "already exists" even
                // though the attachment is now in exactly the desired state. One manager derives one directory
                // from its database file, so the racing attach is always same-path — the error is benign. The
                // INSTALL/LOAD statements completed before ATTACH threw, so the connection is fully usable.
            } catch {
                // Kurrent.Quack's Open() (pinned 0.0.0-alpha.181, DuckDBConnectionPool.cs) does not dispose the
                // freshly-opened physical connection when Initialize throws, which would leak an open native
                // handle and keep the database file locked. Dispose it here before the failure propagates.
                connection.Dispose();
                throw;
            }
        }

        static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}