// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data.Migrations.DuckDB;

/// <summary>
/// The DuckDB adapter's ONE dependency on the host: how to run work against a connection.
/// It owns nothing — no pooling, no lifetime, no catalog knowledge. The connection the host
/// supplies decides where unqualified DDL lands: on Kontext's surface the <c>USE ldb</c>
/// initializer redirects it into the lance namespace, the only durable store.
///
/// The member signatures deliberately match <see cref="KontextDataSource"/>'s execute surface,
/// so implementing this interface there is purely additive.
/// </summary>
public interface IDuckDBSchemaExecutor {
    ValueTask<T> ExecuteAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken cancellationToken = default);

    ValueTask ExecuteAsync(Action<DuckDBAdvancedConnection> operation, CancellationToken cancellationToken = default);
}
