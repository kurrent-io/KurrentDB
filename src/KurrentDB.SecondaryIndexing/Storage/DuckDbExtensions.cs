// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.SecondaryIndexing.Storage;

public static class DuckDbExtensions {
	public static TRow? QueryFirstOrDefault<TRow, TQuery>(this DuckDBConnectionPool pool)
		where TRow : struct
		where TQuery : IQuery<TRow> {
		using (pool.Rent(out var connection)) {
			return connection.QueryFirstOrDefault<TRow, TQuery>();
		}
	}

	static TRow? GetFirstOrDefault<TRow, TParser>(this QueryResult<TRow, TParser> result)
		where TRow : struct where TParser : IQuery<TRow> {
		if (TParser.UseStreamingMode) {
			throw new InvalidOperationException("Streaming mode must be disabled for query with single result");
		}
		using var enumerator = result.GetEnumerator();
		return enumerator.MoveNext() ? enumerator.Current : null;
	}

	public static TRow? QueryFirstOrDefault<TRow, TQuery>(this DuckDBAdvancedConnection connection)
		where TRow : struct
		where TQuery : IQuery<TRow> {
		var result = connection.ExecuteQuery<TRow, TQuery>();
		return result.GetFirstOrDefault();
	}

	public static TRow? QueryFirstOrDefault<TArgs, TRow, TQuery>(this DuckDBConnectionPool pool, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IQuery<TArgs, TRow> {
		using (pool.Rent(out var connection)) {
			return connection.QueryFirstOrDefault<TArgs, TRow, TQuery>(args);
		}
	}

	public static TRow? QueryFirstOrDefault<TArgs, TRow, TQuery>(this DuckDBAdvancedConnection connection, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IQuery<TArgs, TRow> {
		if (TQuery.UseStreamingMode) {
			throw new InvalidOperationException("Streaming mode must be disabled for query with single result");
		}
		var result = connection.ExecuteQuery<TArgs, TRow, TQuery>(args);
		using var enumerator = result.GetEnumerator();
		return enumerator.MoveNext() ? enumerator.Current : null;
	}

	public static List<TRow> Query<TRow, TQuery>(this DuckDBConnectionPool pool)
		where TRow : struct
		where TQuery : IQuery<TRow> {
		using (pool.Rent(out var connection)) {
			return connection.Query<TRow, TQuery>();
		}
	}

	public static List<TRow> Query<TRow, TQuery>(this DuckDBAdvancedConnection connection)
		where TRow : struct
		where TQuery : IQuery<TRow> {
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
		where TQuery : IQuery<TArgs, TRow> {
		using (pool.Rent(out var connection)) {
			return connection.Query<TArgs, TRow, TQuery>(args);
		}
	}

	public static List<TRow> Query<TArgs, TRow, TQuery>(this DuckDBAdvancedConnection connection, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IQuery<TArgs, TRow> {
		List<TRow> elements = [];
		using var result = connection.ExecuteQuery<TArgs, TRow, TQuery>(args).GetEnumerator();

		while (result.MoveNext()) {
			elements.Add(result.Current);
		}

		return elements;
	}

	public static void ExecuteNonQuery<TQuery>(this DuckDBConnectionPool pool) where TQuery : IParameterlessStatement {
		using (pool.Rent(out var connection)) {
			connection.ExecuteNonQuery<TQuery>();
		}
	}

	public static void ExecuteNonQuery<TArgs, TQuery>(this DuckDBConnectionPool pool, TArgs args)
		where TQuery : IPreparedStatement<TArgs> where TArgs : struct {
		using (pool.Rent(out var connection)) {
			connection.ExecuteNonQuery<TArgs, TQuery>(args);
		}
	}
}
