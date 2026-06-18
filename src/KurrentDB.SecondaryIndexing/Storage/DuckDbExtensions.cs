// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Storage;

public static class DuckDbExtensions {
	public static void CopyTo<TArgs, TRow, TQuery>(this QueryResult<TArgs, TRow, TQuery> result, ICollection<TRow> output)
		where TArgs : struct
		where TQuery : IQuery, IBinder<TArgs, PreparedStatement, StatementBindingResult>, IDataRowParser<TRow>, allows ref struct {
		foreach (ref readonly var row in result) {
			output.Add(row);
		}
	}

	public static void CopyTo<TRow, TQuery>(this QueryResult<TRow, TQuery> result, ICollection<TRow> output)
		where TQuery : IQuery, IBinder<ValueTuple, PreparedStatement, StatementBindingResult>, IDataRowParser<TRow>, allows ref struct {
		foreach (ref readonly var row in result) {
			output.Add(row);
		}
	}
}
