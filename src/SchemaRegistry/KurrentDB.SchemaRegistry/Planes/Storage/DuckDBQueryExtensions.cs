// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Data;
using Dapper;
using Kurrent.Quack;

namespace KurrentDB.SchemaRegistry.Planes.Storage;

internal static class DuckDBQueryExtensions {
	public static TResult QueryOne<TResult>(this DuckDBAdvancedConnection cnn, string sql, Func<dynamic?, TResult> map, object? param = null, IDbTransaction? transaction = null) {
		Ensure.NotNullOrWhiteSpace(sql);
		var record = cnn.QueryFirstOrDefault(sql, param, transaction);
		return map(record);
	}

	public static IEnumerable<TResult> QueryMany<TResult>(this DuckDBAdvancedConnection connection, string sql, Func<dynamic, TResult> map, object? param = null) {
		Ensure.NotNullOrWhiteSpace(sql);

		foreach (var record in connection.Query(sql, param))
			yield return map(record);
	}
}
