// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.SecondaryIndexing.Storage;

public static class DuckDbExtensions {
	extension(DuckDBConnectionPool db) {
		public List<TRow> QueryToList<TArgs, TRow, TQuery>(TArgs args)
			where TArgs : struct
			where TRow : struct
			where TQuery : IQuery<TArgs, TRow>
			=> db.QueryAsCollection<TArgs, TRow, TQuery, List<TRow>>(args);

		public List<TRow> QueryToList<TRow, TQuery>()
			where TRow : struct
			where TQuery : IQuery<TRow>
			=> db.QueryAsCollection<TRow, TQuery, List<TRow>>();
	}
}
