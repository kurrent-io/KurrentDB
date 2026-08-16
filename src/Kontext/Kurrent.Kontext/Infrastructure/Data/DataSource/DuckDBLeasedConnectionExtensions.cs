// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;

namespace Kurrent.Quack;

/// <summary>
/// Mirrors the <see cref="DuckDBAdvancedConnection"/> extension surface for leased connections, so
/// a <see cref="DuckDBLeasedConnection"/> is used exactly like the connection it wraps.
/// </summary>
public static class DuckDBLeasedConnectionExtensions {
	/// <inheritdoc cref="DuckDBAdvancedConnection.GetPreparedStatement{TStatement}()"/>
	public static ref PreparedStatement GetPreparedStatement<TStatement>(this DuckDBLeasedConnection connection)
		where TStatement : IPreparedStatement
		=> ref connection.Connection.GetPreparedStatement<TStatement>();

	/// <inheritdoc cref="DuckDBAdvancedConnection.GetPreparedStatement{TStatement}(ref TStatement)"/>
	public static ref PreparedStatement GetPreparedStatement<TStatement>(this DuckDBLeasedConnection connection, ref TStatement statement)
		where TStatement : struct, IDynamicPreparedStatement, IEquatable<TStatement>
		=> ref connection.Connection.GetPreparedStatement(ref statement);

	/// <inheritdoc cref="DuckDBAdvancedConnection.BeginTransaction()"/>
	public static TransactionScope BeginTransaction(this DuckDBLeasedConnection connection)
		=> connection.Connection.BeginTransaction();

	/// <summary>
	/// Executes an ad-hoc statement that returns no rows.
	/// </summary>
	public static long ExecuteAdHocNonQuery(this DuckDBLeasedConnection connection, ReadOnlySpan<byte> statementUtf8NullTerminated)
		=> connection.Connection.ExecuteAdHocNonQuery(statementUtf8NullTerminated);

	/// <summary>
	/// Executes an ad-hoc query.
	/// </summary>
	public static QueryResult ExecuteAdHocQuery(this DuckDBLeasedConnection connection, ReadOnlySpan<byte> queryUtf8NullTerminated)
		=> connection.Connection.ExecuteAdHocQuery(queryUtf8NullTerminated);

	public static long ExecuteNonQuery<TStatement>(this DuckDBLeasedConnection connection)
		where TStatement : IParameterlessStatement
		=> connection.Connection.ExecuteNonQuery<TStatement>();

	public static long ExecuteNonQuery<TArgs, TStatement>(this DuckDBLeasedConnection connection, in TArgs args)
		where TArgs : struct
		where TStatement : IPreparedStatement<TArgs>
		=> connection.Connection.ExecuteNonQuery<TArgs, TStatement>(in args);

	public static QueryResult ExecuteQuery<TQuery>(this DuckDBLeasedConnection connection)
		where TQuery : IParameterlessStatement
		=> connection.Connection.ExecuteQuery<TQuery>();

	public static QueryResult ExecuteQuery<TArgs, TQuery>(this DuckDBLeasedConnection connection, in TArgs args)
		where TArgs : struct
		where TQuery : IPreparedStatement<TArgs>
		=> connection.Connection.ExecuteQuery<TArgs, TQuery>(in args);

	public static QueryResult<TArgs, TRow, TQuery> ExecuteQuery<TArgs, TRow, TQuery>(this DuckDBLeasedConnection connection, in TArgs args)
		where TArgs : struct
		where TQuery : IQuery<TArgs, TRow>
		=> connection.Connection.ExecuteQuery<TArgs, TRow, TQuery>(in args);

	public static QueryResult<TRow, TQuery> ExecuteQuery<TRow, TQuery>(this DuckDBLeasedConnection connection)
		where TQuery : IQuery<TRow>
		=> connection.Connection.ExecuteQuery<TRow, TQuery>();

	public static Optional<TRow> QueryFirstOrDefault<TArgs, TRow, TQuery>(this DuckDBLeasedConnection connection, in TArgs args)
		where TArgs : struct
		where TQuery : IQuery<TArgs, TRow>
		=> connection.Connection.QueryFirstOrDefault<TArgs, TRow, TQuery>(in args);

	public static Optional<TRow> QueryFirstOrDefault<TRow, TQuery>(this DuckDBLeasedConnection connection)
		where TQuery : IQuery<TRow>
		=> connection.Connection.QueryFirstOrDefault<TRow, TQuery>();

	public static QueryResult<TArgs, TRow, TQuery> ExecuteQuery<TArgs, TRow, TQuery>(
		this DuckDBLeasedConnection connection, ref TQuery query, in TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : struct, IDynamicQuery<TArgs, TRow>, IEquatable<TQuery>
		=> connection.Connection.ExecuteQuery<TArgs, TRow, TQuery>(ref query, in args);

	public static QueryResult<TRow, TQuery> ExecuteQuery<TRow, TQuery>(
		this DuckDBLeasedConnection connection, ref TQuery query)
		where TRow : struct
		where TQuery : struct, IDynamicQuery<TRow>, IEquatable<TQuery>
		=> connection.Connection.ExecuteQuery<TRow, TQuery>(ref query);

	public static long ExecuteNonQuery<TStatement>(this DuckDBLeasedConnection connection, ref TStatement statement)
		where TStatement : struct, IDynamicParameterlessStatement, IEquatable<TStatement>
		=> connection.Connection.ExecuteNonQuery(ref statement);

	public static long ExecuteNonQuery<TArgs, TStatement>(this DuckDBLeasedConnection connection, ref TStatement statement, in TArgs args)
		where TArgs : struct
		where TStatement : struct, IDynamicPreparedStatement<TArgs>, IEquatable<TStatement>
		=> connection.Connection.ExecuteNonQuery<TArgs, TStatement>(ref statement, in args);
}
