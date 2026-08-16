// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Quack;

/// <summary>
/// A connection leased from a <see cref="DuckDBDataSource"/>. Disposing it returns the underlying
/// connection to the data source and makes this instance unusable: every member, including the
/// implicit conversion, throws <see cref="ObjectDisposedException"/> afterwards.
/// </summary>
/// <remarks>
/// Pass it directly wherever a <see cref="DuckDBAdvancedConnection"/> is expected — the implicit
/// conversion hands over the live connection. A converted reference is the raw connection: do not
/// keep it beyond this lease's disposal.
/// </remarks>
public sealed class DuckDBLeasedConnection : IDisposable, IAsyncDisposable {
    DuckDBAdvancedConnection?  _inner;
    readonly DuckDBDataSource? _owner; // null for a dedicated connection: disposal destroys it

    internal DuckDBLeasedConnection(DuckDBAdvancedConnection inner, DuckDBDataSource? owner) {
        _inner = inner;
        _owner = owner;
    }

    /// <summary>
    /// Gets the leased connection.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public DuckDBAdvancedConnection Connection => _inner ?? throw new ObjectDisposedException(nameof(DuckDBLeasedConnection));

    public System.Data.ConnectionState State => Connection.State;

    public string ServerVersion => Connection.ServerVersion;

    public string DataSource => Connection.DataSource;

    public TransactionScope BeginTransaction() => Connection.BeginTransaction();

    public bool HasOpenTransaction => Connection.UnsafeHasOpenTransaction();

    /// <summary>
    /// Ends the lease. A pooled connection goes back to its data source with its prepared-statement
    /// cache intact; a dedicated one is destroyed. Disposing twice is harmless.
    /// </summary>
    public void Dispose() {
        if (_inner is not { } inner)
            return;

        _inner = null;

        if (_owner is { } owner)
            owner.Release(inner);
        else
            inner.Dispose();
    }

    /// <inheritdoc cref="Dispose"/>
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public static implicit operator DuckDBAdvancedConnection(DuckDBLeasedConnection connection) => connection.Connection;
}