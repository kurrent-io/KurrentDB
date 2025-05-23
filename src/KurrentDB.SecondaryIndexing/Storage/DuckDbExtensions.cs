// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.SecondaryIndexing.Storage;

public static class DuckDbExtensions {
	public static TRow? QueryFirstOrDefault<TRow, TQuery>(this DuckDBConnectionPool pool)
		where TRow : struct
		where TQuery : IParameterlessStatement, IDataRowParser<TRow> {
		using (pool.Rent(out var connection)) {
			return connection.QueryFirstOrDefault<TRow, TQuery>();
		}
	}

	public static TRow? QueryFirstOrDefault<TRow, TQuery>(this DuckDBAdvancedConnection connection)
		where TRow : struct
		where TQuery : IParameterlessStatement, IDataRowParser<TRow> {
		using var result = connection.ExecuteQuery<TRow, TQuery>().GetEnumerator();

		return result.MoveNext() ? null : result.Current;
	}

	public static TRow? QueryFirstOrDefault<TArgs, TRow, TQuery>(this DuckDBConnectionPool pool, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IPreparedStatement<TArgs>, IDataRowParser<TRow> {
		using (pool.Rent(out var connection)) {
			return connection.QueryFirstOrDefault<TArgs, TRow, TQuery>(args);
		}
	}

	public static TRow? QueryFirstOrDefault<TArgs, TRow, TQuery>(this DuckDBAdvancedConnection connection, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IPreparedStatement<TArgs>, IDataRowParser<TRow> {
		using var result = connection.ExecuteQuery<TArgs, TRow, TQuery>(args).GetEnumerator();

		return result.MoveNext() ? null : result.Current;
	}

	public static List<TRow> Query<TRow, TQuery>(this DuckDBConnectionPool pool)
		where TRow : struct
		where TQuery : IDataRowParser<TRow>, IParameterlessStatement {
		using (pool.Rent(out var connection)) {
			return connection.Query<TRow, TQuery>();
		}
	}


	public static List<TRow> Query<TRow, TQuery>(this DuckDBAdvancedConnection connection)
		where TRow : struct
		where TQuery : IDataRowParser<TRow>, IParameterlessStatement {
		List<TRow> elements = [];
		using var result = connection.ExecuteQuery<TRow, TQuery>().GetEnumerator();

		while (result.MoveNext()) {
			elements.Add(result.Current);
		}

		return elements;
	}

	public static List<TRow> Query<TArgs, TRow, TQuery>(this DuckDBConnectionPool pool, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IPreparedStatement<TArgs>, IDataRowParser<TRow> {
		using (pool.Rent(out var connection)) {
			return connection.Query<TArgs, TRow, TQuery>(args);
		}
	}


	public static List<TRow> Query<TArgs, TRow, TQuery>(this DuckDBAdvancedConnection connection, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IPreparedStatement<TArgs>, IDataRowParser<TRow> {
		List<TRow> elements = [];
		using var result = connection.ExecuteQuery<TArgs, TRow, TQuery>(args).GetEnumerator();

		while (result.MoveNext()) {
			elements.Add(result.Current);
		}

		return elements;
	}

	public static void ExecuteNonQuery<TQuery>(this DuckDBConnectionPool pool)
		where TQuery : IParameterlessStatement {
		using (pool.Rent(out var connection)) {
			connection.ExecuteNonQuery<TQuery>();
		}
	}
}
