// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace Kurrent.Kontext.Infrastructure.Data;

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
	public DuckDBScopedConnection OpenConnection(ConnectionUse use = ConnectionUse.ReadOnly) =>
		use switch{
			ConnectionUse.Dedicated      => OpenDedicatedConnection(),
			ConnectionUse.ReadOnly => OpenReadOnlyConnection(),
			_ => throw new ArgumentOutOfRangeException(nameof(use), use, "Unknown connection use."),
		};

	/// <summary>
	/// Opens a dedicated connection. The caller owns the connection and must dispose it when done.
	/// </summary>
	/// <returns></returns>
	public DuckDBScopedConnection OpenDedicatedConnection() =>
		new(_pool.Open(), scope: null);

	/// <summary>
	/// Opens a pooled connection for concurrent reads. The caller must dispose the returned handle when done,
	/// </summary>
	/// <returns></returns>
	public DuckDBScopedConnection OpenReadOnlyConnection() {
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
/// Describes the intended use of a connection obtained from <see cref="KontextConnectionProvider.OpenConnection"/>.
/// </summary>
public enum ConnectionUse {
	/// <summary>
	/// The connection is used for potentially concurrent reads. It is rented from the pool,
	/// and disposing the handle returns it to the pool instead of destroying it.
	/// </summary>
	ReadOnly = 0,

	/// <summary>
	/// The connection is dedicated to the caller — typically a writer or a sequential reader.
	/// It is not pooled; the caller owns it for its lifetime, and disposing the handle destroys it.
	/// </summary>
	Dedicated,
}

/// <summary>
/// Represents a disposable handle over a connection obtained from <see cref="Connection"/>.
/// Disposing the handle returns a pooled connection to its pool, or destroys a dedicated one.
/// </summary>
/// <remarks>
/// Dispose the handle exactly once. The underlying <see cref="KontextConnectionProvider"/> must not be disposed
/// directly and must not be used after the handle is disposed.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct DuckDBScopedConnection : IDisposable {
	readonly IDisposable? _scope; // null for dedicated connections

	internal DuckDBScopedConnection(DuckDBAdvancedConnection connection, IDisposable? scope) {
		Connection = connection;
		_scope = scope;
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

// public partial class DuckDBAdvancedConnection {
// 	IDisposable? _scope; // null for dedicated connections
//
// 	internal void SetScope(IDisposable? scope) => _scope = scope;
//
// 	public override void Close() {
// 		if (_scope is null)
// 			base.Close();
// 		else
// 			_scope.Dispose();
// 	}
//
// 	public override Task CloseAsync() {
// 		if (_scope is null)
// 			return base.CloseAsync();
//
// 		_scope.Dispose();
// 		return Task.CompletedTask;
// 	}
// }
//
// public class DuckDBConnectionProviderV2 : Disposable {
// 	readonly DuckDBConnectionPool _pool;
// 	readonly bool _ownsPool;
//
// 	/// <summary>
// 	/// Initializes the provider over an existing pool. The caller retains ownership of the pool.
// 	/// </summary>
// 	/// <param name="pool">The pool to obtain connections from.</param>
// 	/// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
// 	public DuckDBConnectionProviderV2(DuckDBConnectionPool pool) => _pool = pool;
//
// 	/// <summary>
// 	/// Initializes the provider with its own pool. The pool is disposed together with the provider.
// 	/// </summary>
// 	/// <param name="connectionString">The connection string.</param>
// 	public DuckDBConnectionProviderV2(string connectionString) : this(new DuckDBConnectionPool(connectionString)) =>
// 		_ownsPool = true;
//
// 	/// <summary>
// 	/// Gets a connection for the specified use.
// 	/// </summary>
// 	/// <param name="use">The intended use of the connection.</param>
// 	/// <returns>The connection handle. The caller must dispose it exactly once.</returns>
// 	public DuckDBAdvancedConnection OpenConnection(ConnectionUse use = ConnectionUse.ReadOnly) =>
// 		use switch{
// 			ConnectionUse.Dedicated => OpenDedicatedConnection(),
// 			ConnectionUse.ReadOnly  => OpenReadOnlyConnection(),
// 			_ => throw new ArgumentOutOfRangeException(nameof(use), use, "Unknown connection use."),
// 		};
//
// 	/// <summary>
// 	/// Opens a dedicated connection. The caller owns the connection and must dispose it when done.
// 	/// </summary>
// 	/// <returns></returns>
// 	public DuckDBAdvancedConnection OpenDedicatedConnection() =>
// 		_pool.Open();
//
// 	/// <summary>
// 	/// Opens a pooled connection for concurrent reads. The caller must dispose the returned handle when done,
// 	/// </summary>
// 	/// <returns></returns>
// 	public DuckDBAdvancedConnection OpenReadOnlyConnection() {
// 		var scope = _pool.Rent(out var connection);
// 		connection.SetScope(scope);
// 		return connection;
// 	}
//
// 	protected override void Dispose(bool disposing) {
// 		if (disposing && _ownsPool)
// 			_pool.Dispose();
//
// 		base.Dispose(disposing);
// 	}
// }
