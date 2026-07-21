// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext;

namespace Kurrent.Quack.ConnectionPool;

/// <summary>
/// Provides connections without exposing the pooling policy to the caller: concurrent reads are
/// rented from the underlying pool, writers get dedicated connections, and disposing the returned
/// <see cref="DuckDBScopedConnection"/> handle always does the right thing for how the connection
/// was obtained.
/// </summary>
public class KontextConnectionProvider : Disposable {
	readonly DuckDBConnectionPool _pool;
	readonly bool _ownsPool;

	/// <summary>
	/// Initializes the provider over an existing pool. The caller retains ownership of the pool.
	/// </summary>
	/// <param name="pool">The pool to obtain connections from.</param>
	/// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
	public KontextConnectionProvider(DuckDBConnectionPool pool) => _pool = pool;

	/// <summary>
	/// Initializes the provider with its own pool. The pool is disposed together with the provider.
	/// </summary>
	/// <param name="connectionString">The connection string.</param>
	public KontextConnectionProvider(string connectionString) : this(new DuckDBConnectionPool(connectionString)) =>
		_ownsPool = true;

	/// <summary>
	/// Gets a connection for the specified use.
	/// </summary>
	/// <param name="use">The intended use of the connection.</param>
	/// <returns>The connection handle. The caller must dispose it exactly once.</returns>
	public DuckDBScopedConnection GetConnection(ConnectionUse use = ConnectionUse.ConcurrentRead) {
		if (use is ConnectionUse.Dedicated)
			return new(_pool.Open(), scope: null);

		var scope = _pool.Rent(out var connection);
		return new(connection, scope);
	}

	protected override void Dispose(bool disposing) {
		if (disposing && _ownsPool)
			_pool.Dispose();

		base.Dispose(disposing);
	}
}

/// <summary>
/// Represents a disposable handle over a connection obtained from <see cref="KontextConnectionProvider"/>.
/// Disposing the handle returns a pooled connection to its pool, or destroys a dedicated one.
/// </summary>
/// <remarks>
/// Dispose the handle exactly once. The underlying <see cref="Connection"/> must not be disposed
/// directly and must not be used after the handle is disposed.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct DuckDBScopedConnection : IDisposable {
    readonly IDisposable? _scope; // null for dedicated connections

    internal DuckDBScopedConnection(DuckDBAdvancedConnection connection, IDisposable? scope) {
        Connection = connection;
        _scope     = scope;
    }

    /// <summary>
    /// Gets the underlying connection.
    /// </summary>
    public DuckDBAdvancedConnection Connection { get; }

    public static implicit operator DuckDBAdvancedConnection(DuckDBScopedConnection connection) =>
        connection.Connection;

    /// <inheritdoc/>
    public void Dispose() {
        if (_scope is not null) {
            // returns the connection to the pool; if the pool is full, the scope destroys it
            _scope.Dispose();
        } else {
            Connection?.Dispose();
        }
    }
}

/// <summary>
/// Describes the intended use of a connection obtained from <see cref="KontextConnectionProvider.GetConnection"/>.
/// </summary>
public enum ConnectionUse {
    /// <summary>
    /// The connection is used for potentially concurrent reads. It is rented from the pool,
    /// and disposing the handle returns it to the pool instead of destroying it.
    /// </summary>
    ConcurrentRead = 0,

    /// <summary>
    /// The connection is dedicated to the caller — typically a writer or a sequential reader.
    /// It is not pooled; the caller owns it for its lifetime, and disposing the handle destroys it.
    /// </summary>
    Dedicated,
}

