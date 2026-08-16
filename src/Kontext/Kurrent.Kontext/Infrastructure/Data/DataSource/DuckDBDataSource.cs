// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Data;
using System.Runtime.CompilerServices;
using DotNext;
using DotNext.Collections.Concurrent;
using DuckDB.NET.Data;

namespace Kurrent.Quack;

/// <summary>
/// A pool of <see cref="DuckDBAdvancedConnection"/> instances, leased through
/// <see cref="DuckDBLeasedConnection"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpenConnection"/> hands back a lease. Disposing one obtained with
/// <see cref="ConnectionUse.Pooled"/> returns the underlying connection to this pool with its
/// prepared-statement cache intact; disposing one obtained with <see cref="ConnectionUse.Dedicated"/>
/// destroys it. Either way <see langword="using"/> is the correct thing to write, and a disposed
/// lease throws <see cref="ObjectDisposedException"/> on every use. This mirrors <c>SqlConnection</c>,
/// where disposal returns the connection to its pool rather than tearing it down.
/// </para>
/// <para>
/// The pool is safe for concurrent use. Individual connections are not: a connection is yours
/// alone until you release it.
/// </para>
/// </remarks>
public class DuckDBDataSource : Disposable {
    readonly BoundedObjectPool<DuckDBAdvancedConnection> _pool;

    readonly Func<DuckDBAdvancedConnection> _create;
    readonly DuckDBConnection?              _ownedPrototype; // non-null only when we created it

    readonly SqlStatements                     _sql;
    readonly Action<DuckDBAdvancedConnection>? _optionsInitializer;
    readonly bool                              _lockConfiguration;
    readonly string[]                          _attachAliases;
    bool                                       _installed;

    /// <summary>
    /// Initializes a pool over the database named by
    /// <see cref="DuckDBDataSourceOptions.ConnectionString"/>.
    /// </summary>
    /// <param name="options">The options, validated through <see cref="DuckDBDataSourceOptions.EnsureValid"/>.</param>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public DuckDBDataSource(DuckDBDataSourceOptions options) {
        options.EnsureValid();

        _sql                = options.GenerateSqlStatements();
        _optionsInitializer = options.Initializer;
        _lockConfiguration  = options.LockConfiguration;
        _attachAliases      = [.. options.AttachedDatabases.Select(static attached => attached.Alias)];

        ConnectionString = options.ToConnectionString();

        if (options.IsInMemory) {
            // Every connection has to share one in-memory database, so hold it open for our lifetime.
            var prototype = new DuckDBConnection(ConnectionString);
            prototype.Open();

            _ownedPrototype = prototype;
            _create = () => Duplicate(prototype);
        } else {
            var effectiveConnectionString = ConnectionString;
            _create = () => new() { ConnectionString = effectiveConnectionString };
        }

        _pool = new(options.MaxIdleConnections);
        
        // Creates a connection that joins the prototype's in-memory database rather than resolving one of its own.
        static DuckDBAdvancedConnection Duplicate(DuckDBConnection prototype) {
            var connection = new DuckDBAdvancedConnection { ConnectionString = prototype.ConnectionString };
            InMemoryConnectionState.Copy(prototype, connection);
            return connection;
        }
    }

    /// <summary>
    /// Gets the connection string every connection from this data source is opened with.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Gets the maximum number of idle connections retained for reuse. A connection released while
    /// the pool already holds this many is destroyed instead of retained.
    /// </summary>
    /// <remarks>
    /// This does not cap how many connections may exist at once: <see cref="OpenConnection"/> always creates
    /// one when none is idle. The value assigned is a requested bound that the underlying pool may
    /// round up, so read the property back for the effective value.
    /// </remarks>
    public int MaxIdleConnections => _pool.Capacity;

    /// <summary>
    /// Gets a connection for the specified use.
    /// </summary>
    /// <param name="use">How the caller intends to use the connection.</param>
    /// <returns>An opened connection lease. Dispose it when done.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="use"/> is not a known value.</exception>
    /// <exception cref="ObjectDisposedException">The pool is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // a thin dispatcher: the switch folds away when `use` is a constant
    public DuckDBLeasedConnection OpenConnection(ConnectionUse use = ConnectionUse.Pooled) =>
        use switch {
            ConnectionUse.Pooled    => OpenPooledConnection(),
            ConnectionUse.Dedicated => OpenDedicatedConnection(),
            _                       => throw new ArgumentOutOfRangeException(nameof(use), use, "Unknown connection use.")
        };

    /// <summary>
    /// Gets a pooled connection — typically a concurrent reader. Disposing the lease returns the
    /// connection to the pool with its prepared-statement cache intact.
    /// </summary>
    /// <returns>An opened connection lease. Dispose it when done.</returns>
    /// <exception cref="ObjectDisposedException">The pool is disposed.</exception>
    public DuckDBLeasedConnection OpenPooledConnection() {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new(_pool.TryGet() ?? Connect(), owner: this);
    }

    /// <summary>
    /// Gets a connection the caller owns alone — typically a writer or a sequential reader.
    /// It is never pooled; disposing the lease destroys it.
    /// </summary>
    /// <returns>An opened connection lease. Dispose it when done.</returns>
    /// <exception cref="ObjectDisposedException">The pool is disposed.</exception>
    public DuckDBLeasedConnection OpenDedicatedConnection() {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new(Connect(), owner: null);
    }

    /// <summary>
    /// Reclaims the connection behind a disposed lease.
    /// </summary>
    internal void Release(DuckDBAdvancedConnection connection) {
        // A transaction left open would fail the next caller's BEGIN, so unwind it first,
        // destroying the connection when the unwind fails.
        if (!connection.UnsafeTryReset()) {
            connection.Dispose();
            return;
        }

        // A connection physically closed mid-lease must not be pooled. Otherwise retain for reuse,
        // or destroy when the pool is full, frozen, or already disposed.
        if (connection.State is not ConnectionState.Open || !_pool.TryReturn(connection))
            connection.Dispose();
    }

    /// <summary>
    /// Initializes a newly created connection before it is handed to a caller.
    /// </summary>
    /// <remarks>
    /// The default implementation does nothing. Override to register table functions, apply
    /// <c>SET</c> pragmas, and so on.
    /// </remarks>
    /// <param name="connection">The connection to initialize.</param>
    protected virtual void Initialize(DuckDBAdvancedConnection connection) { }

    /// <summary>
    /// Creates, opens and initializes a connection, destroying it when either step throws so it is
    /// not leaked open with nobody able to release it. NoInlining: this is the miss path, and letting
    /// it inline into <see cref="OpenConnection"/> costs 2 ns on every pool hit. Measured, not guessed.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    DuckDBAdvancedConnection Connect() {
        try {
            return ConnectOnce();
        } catch (Exception ex) when (IsBenignAttachRace(ex)) {
            // Core ATTACH IF NOT EXISTS is atomic, but a storage-extension attach (TYPE LANCE) is
            // not: it skips the engine's path reservation and does namespace I/O between the
            // exists-check and registration, so concurrent first connections race and the loser dies
            // mid-script with its settings unapplied. The winner's attachment is exactly the
            // desired state, so one fresh attempt replays the full script against it — one retry is
            // enough, the pre-check short-circuits once any winner exists.
            return ConnectOnce();
        }
        
        DuckDBAdvancedConnection ConnectOnce() {
            var connection = _create();

            try {
                connection.Open();
                var locked = ApplyOptions(connection);
                Initialize(connection);
                _optionsInitializer?.Invoke(connection);

                // Emitted after the initializers so callbacks can still SET; nothing can after this.
                if (_lockConfiguration && !locked)
                    connection.ExecuteAdHocNonQuery("SET GLOBAL lock_configuration = true;"u8);
            } catch {
                connection.Dispose();
                throw;
            }

            return connection;
        }
        
        // The race loser's error names one of this data source's own aliases; anything else is a real
        // failure and must propagate.
        bool IsBenignAttachRace(Exception ex) => 
            _attachAliases.Any(alias => ex.Message.Contains($"database with name \"{alias}\" already exists", StringComparison.Ordinal));
        
        bool ApplyOptions(DuckDBAdvancedConnection connection) {
            if (_sql.IsEmpty && !_lockConfiguration)
                return false;

            // The unsynchronized flag is deliberate: concurrent first connections may both install,
            // and INSTALL is idempotent, so the race is benign and cheaper than a lock. A failed
            // install leaves the flag down and the next connection retries.
            if (!Volatile.Read(ref _installed) && _sql.InstallExtensions.Length > 0) {
                connection.ExecuteAdHocNonQuery(_sql.InstallExtensions.AsSpan(), multipleStatements: true);
                Volatile.Write(ref _installed, true);
            }

            // A locked instance is one this data source configured on an earlier connection: DuckDB
            // rejects every SET once lock_configuration is on, so the settings-free script replays as
            // fidelity - the settings already hold the locked-in values - while loads and attachments,
            // which the lock permits, still run. Either way it is a single pre-composed batch.
            var locked = _lockConfiguration && IsConfigurationLocked(connection);
            var script = locked ? _sql.ForLockedConnection : _sql.ForConnection;

            if (script.Length > 0)
                connection.ExecuteAdHocNonQuery(script.AsSpan(), multipleStatements: true);

            return locked;

            static bool IsConfigurationLocked(DuckDBAdvancedConnection connection) {
                using var result = connection.ExecuteAdHocQuery("SELECT current_setting('lock_configuration')::BIGINT"u8);

                if (!result.TryFetch(out var chunk))
                    return false;

                var locked = chunk[0].Int64Rows[0] is 1;
                chunk.Dispose();
                return locked;
            }
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (disposing) {
            // Drain before freezing. BoundedObjectPool.Freeze() writes a sentinel into the single
            // fast-item slot introduced in DotNext 6.2.0, discarding whatever connection is held
            // there, so freezing first deterministically orphans one connection and leaves it open.
            // Freeze after the first drain to stop further returns, then drain again to catch anything
            // that landed in the ring buffer in between.
            //
            // One narrow race survives and cannot be closed from here: a release that lands in the
            // fast-item slot after the first drain succeeds, and Freeze() then overwrites it, so the
            // second drain cannot see it and the releaser never learns it failed. That connection is
            // then unreachable and stays open until its SafeHandles are finalized. Fixing it properly
            // needs Freeze() to hand the fast item back, which is an upstream change.
            Drain();
            _pool.Freeze();
            Drain();

            _ownedPrototype?.Dispose();
        }

        base.Dispose(disposing);

        void Drain() {
            while (_pool.TryGet() is { } connection) {
                connection.Dispose();
            }
        }
    }
}

/// <summary>
/// Describes the intended use of a connection obtained from <see cref="DuckDBDataSource.OpenConnection"/>.
/// </summary>
public enum ConnectionUse {
    /// <summary>
    /// The connection is drawn from the data source's pool — typically a concurrent reader.
    /// Disposing it releases it back for reuse rather than destroying it.
    /// </summary>
    /// <remarks>
    /// The data source never hands one connection to two callers at once, but the connection itself
    /// is not thread-safe: it is yours alone until you release it, and must not be touched from
    /// another thread in the meantime.
    /// </remarks>
    Pooled = 0,

    /// <summary>
    /// The connection belongs to the caller alone — typically a writer or a sequential reader. It is
    /// not pooled; the caller owns it for its lifetime, and disposing it destroys it.
    /// </summary>
    Dedicated,
}