// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.SecondaryIndexing.Storage;

public static class DuckDbExtensions {
	public static List<TRow> QueryToList<TArgs, TRow, TQuery>(this DuckDBConnectionPool db, TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IQuery<TArgs, TRow>
		=> db.QueryAsCollection<TArgs, TRow, TQuery, List<TRow>>(args);
}
