// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Polly;
using Polly.Retry;

namespace Kurrent.Kontext.Infrastructure.Data;

/// <summary>
/// Kontext's own DuckDB connection pool: one owner for every engine problem. The engine needs no
/// file — everything durable lives in lance, so every connection opens an in-memory catalog and is
/// bootstrapped with the lance extension and the storage namespace ATTACH (verified engine-side).
/// <see cref="ExecuteAsync{T}"/> is the concurrent READ surface, recycling a stale cached dataset
/// view onto a fresh connection. Writers never rent: they hold a dedicated connection
/// (<see cref="DuckDBConnectionPool{T}.Open"/>) for prepared-statement reuse and transaction
/// control, and carry the Lance commit-conflict retry there.
/// </summary>
public sealed class KontextConnectionPool : DuckDBConnectionPool {
    // Recycles a poisoned connection ONCE: a stale cached dataset view never converges on the same
    // connection, so the rent-and-run callback disposes it (dropping its Scope so the pool can't
    // re-admit it) and the retry re-runs the WHOLE callback — a fresh rent whose Initialize
    // re-ATTACHes a current view. No delay: the fresh connection either sees the dataset or the
    // failure is not transient.
    static readonly ResiliencePipeline StaleHandleRecycle = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions {
            ShouldHandle     = new PredicateBuilder().Handle<Exception>(IsStaleDatasetHandle),
            MaxRetryAttempts = 1,
            Delay            = TimeSpan.Zero,
        })
        .Build();

    // The engine catalog is in-memory, per connection: everything durable lives in lance (tables
    // AND checkpoints), so the duck engine is a pure compute surface — pinned by
    // InMemoryEngineProbeTests.
    const string InMemoryEngine = "Data Source=:memory:";

    readonly string _initializeSql;

    // Set by Dispose; guards ExecuteAsync against renting from a frozen pool (Kurrent.Quack's
    // Rent/Open perform no disposed-check of their own — a rent on a frozen pool silently mints a
    // brand-new connection).
    volatile bool _disposed;

    public KontextConnectionPool(string storagePath)
        : base(VendoredDuckDBExtensions.AmendConnectionString(InMemoryEngine)) {
        ArgumentException.ThrowIfNullOrEmpty(storagePath);

        StoragePath  = Path.GetFullPath(storagePath);
        StorageAlias = "ldb";

        // ATTACH does not create the namespace directory. Pure filesystem work, so construction
        // stays free of DB I/O — engine problems surface on the first operation, not here.
        Directory.CreateDirectory(StoragePath);

        // Composed ONCE at construction, never per call: paths and identifiers cannot be bound as
        // parameters in LOAD/ATTACH, so the path is quote-escaped and the alias was validated as a
        // bare identifier above. ATTACH IF NOT EXISTS is idempotent per shared engine instance
        // (a bare ATTACH fails on the second connection). Never DETACH.
        _initializeSql =
            $"""
             {VendoredDuckDBExtensions.EnsureLanceInstalled()}
             ATTACH IF NOT EXISTS '{Escape(StoragePath)}' AS {StorageAlias} (TYPE LANCE);
             """;
        
        static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
    
    /// <summary>The Lance namespace directory holding every collection's dataset.</summary>
    public string StoragePath { get; }
    
    /// <summary>The ATTACH alias under which the Lance namespace is mounted on every pooled connection.</summary>
    public string StorageAlias { get; }

    /// <summary>
    /// Opens a dedicated writer connection with the session redirected into the Lance catalog.
    /// The appender's C API has no catalog slot, so USE is the only route into Lance for it —
    /// and a transaction that writes Lance cannot touch any other attached database, so a
    /// checkpoint store on this connection lands its unqualified tables in the SAME catalog,
    /// which is exactly what lets a batch and its checkpoint share one transaction.
    /// Unqualified names on this connection resolve into Lance, not the engine catalog.
    /// </summary>
    public DuckDBAdvancedConnection OpenLanceWriter() {
        var connection = Open();

        try {
            using var command = connection.CreateCommand();
            command.CommandText = $"USE {StorageAlias}";
            command.ExecuteNonQuery();
            return connection;
        } catch {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Runs a synchronous READ against a rented pooled connection, inline on the caller's thread.</summary>
    /// <remarks>
    /// Reads only by design: renting is the concurrent, stateless surface, so no commit-conflict
    /// handling lives here — reads never commit. The writer (the projector) holds its own dedicated
    /// connection instead and applies the commit-conflict retry around its commits.
    /// </remarks>
    public Task<T> ExecuteAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken = default) {
        // DuckDB has no true async API, so this async surface runs its synchronous work INLINE on
        // the caller's thread and returns an already-completed task; faults and cancellation
        // surface through the returned task.
        ObjectDisposedException.ThrowIf(_disposed, this);

        try {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(StaleHandleRecycle.Execute(_ => RentAndRun(operation), cancellationToken));
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Only the CALLER's token maps to a canceled task; an operation's internal
            // OperationCanceledException is a fault like any other.
            return Task.FromCanceled<T>(cancellationToken);
        } catch (Exception ex) {
            return Task.FromException<T>(ex);
        }
    }

    /// <summary>Runs a synchronous READ with no result against a rented pooled connection.</summary>
    public Task ExecuteAsync(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken = default) =>
        ExecuteAsync<object?>(
            operation: connection => {
                operation(connection);
                return null;
            },
            cancellationToken);

    // One resilience attempt: rent, run, return — with the poisoned-connection discipline.
    T RentAndRun<T>(Func<DuckDBAdvancedConnection, T> operation) {
        // Re-checked on EVERY attempt: a retry racing Dispose must not rent from a frozen pool
        // (Kurrent.Quack's Rent performs no disposed-check — it would silently mint a connection).
        ObjectDisposedException.ThrowIf(_disposed, this);

        // NEVER copy the Scope: it is a struct whose Dispose calls the pool's duplicate-unchecked
        // TryReturn — disposing two copies would admit the same (not-thread-safe) connection twice.
        var scope        = Rent(out var connection);
        var returnToPool = true;

        try {
            return operation(connection);
        } catch (Exception ex) when (IsStaleDatasetHandle(ex)) {
            // Poisoned connection (dead cached dataset view): never return it to the pool —
            // dropping the Scope un-disposed is what keeps it out (Scope.Dispose() is the pool's
            // only TryReturn caller, and the pool keeps no outstanding accounting) — and dispose it
            // so the native handle is released. The pipeline decides whether a fresh attempt runs.
            returnToPool = false;
            connection.Dispose();
            throw;
        } finally {
            if (returnToPool)
                ((IDisposable)scope).Dispose();
        }
    }

    // A dead cached dataset view, in either validated shape: the vacuum/rewrite shape
    // ("LanceError(IO)" + "Not found") or the concurrent-writer shape ("... belongs to
    // non-existent fragment ..."). Only a fresh connection (re-ATTACH) converges.
    static bool IsStaleDatasetHandle(Exception ex) {
        var text = ex.ToString();

        return (text.Contains("LanceError(IO)", StringComparison.Ordinal) && text.Contains("Not found", StringComparison.Ordinal))
            || text.Contains("belongs to non-existent fragment", StringComparison.Ordinal);
    }

    protected override void Initialize(DuckDBAdvancedConnection connection) {
        try {
            try {
                using var command = connection.CreateCommand();
                command.CommandText = _initializeSql;
                command.ExecuteNonQuery();
            } catch (DuckDBException ex) when (ex.Message.Contains($"database with name \"{StorageAlias}\" already exists", StringComparison.Ordinal)) {
                // Two pool-miss connections can race Initialize on the same shared engine instance:
                // ATTACH IF NOT EXISTS is check-then-create, not atomic, so the race loser throws
                // "already exists" even though the attachment is now in exactly the desired state.
                // One pool derives one namespace, so the racing attach is always same-path — benign.
            }

            VerifyLanceNamespace(connection);
        } catch {
            // Kurrent.Quack's Open() does not dispose the freshly-opened physical connection when
            // Initialize throws, which would leak an open native handle. Dispose it here before
            // the failure propagates.
            connection.Dispose();
            throw;
        }
    }

    // The engine-side attach guard: ask the engine whether the alias actually resolved.
    // NOTE (validated live 2026-07-20): an attached TYPE LANCE database reports type='duckdb' in
    // duckdb_databases() on this extension build, so the type column cannot prove the attach —
    // presence by name is the provable fact. The in-memory engine catalog is always named
    // "memory", so the alias can never collide with it.
    void VerifyLanceNamespace(DuckDBAdvancedConnection connection) {
        var info = DuckDBEngineInfo.From(connection);

        if (info.FindDatabase(StorageAlias) is null) {
            throw new InvalidOperationException(
                $"The '{StorageAlias}' Lance namespace is not attached — the pool's connection initialization did not land.");
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing)
            _disposed = true;

        // The base freezes the pool and disposes every idle pooled connection. Each in-memory
        // engine catalog dies with its connection; everything durable is already in lance on disk.
        base.Dispose(disposing);
    }
}
