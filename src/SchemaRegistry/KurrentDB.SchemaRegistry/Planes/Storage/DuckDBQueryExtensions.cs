// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Data;
using Dapper;
using Kurrent.Quack;

namespace KurrentDB.SchemaRegistry.Planes.Storage;

internal static class DuckDBQueryExtensions {
	public static TResult QueryOne<TResult>(this DuckDBAdvancedConnection cnn,
		string sql,
		Func<dynamic?, TResult> map,
		object? param = null,
		IDbTransaction? transaction = null) {
		Ensure.NotNullOrWhiteSpace(sql);
		var record = cnn.QueryFirstOrDefault(sql, param, transaction);
		return map(record);
	}

	public static IEnumerable<TResult> QueryMany<TResult>(this DuckDBAdvancedConnection connection,
		string sql,
		Func<dynamic, TResult> map,
		object? param = null) {
		Ensure.NotNullOrWhiteSpace(sql);

		foreach (var record in connection.Query(sql, param))
			yield return map(record);
	}

	public static TCollection QueryAsCollection<TArgs, TRow, TQuery, TCollection>(this DuckDBAdvancedConnection connection, in TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IQuery<TArgs, TRow>
		where TCollection : ICollection<TRow>, new() {
		var collection = new TCollection();

		foreach (var row in connection.ExecuteQuery<TArgs, TRow, TQuery>(in args)) {
			collection.Add(row);
		}

		return collection;
	}

	public static List<TRow> QueryToList<TArgs, TRow, TQuery>(this DuckDBAdvancedConnection connection, TArgs args)
		where TArgs : struct where TRow : struct where TQuery : IQuery<TArgs, TRow>
		=> connection.QueryAsCollection<TArgs, TRow, TQuery, List<TRow>>(args).ToList();
}
