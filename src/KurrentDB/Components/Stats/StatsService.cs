// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using Dapper;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.Components.Stats.StatsSql;

namespace KurrentDB.Components.Stats;

internal class StatsService(DuckDBConnectionPool db) {
	public IEnumerable<CategoryName> GetCategories() {
		using var connection = db.Open();
		var st = new PreparedStatement(connection, GetAllCategories.CommandText);
		foreach (var row in new QueryResult<CategoryName, GetAllCategories>(st)) {
			yield return row;
		}
	}

	public List<GetCategoryStats.Result> GetCategoryStats(string category)
		=> string.IsNullOrWhiteSpace(category)
			? []
			: db.QueryToList<GetCategoryStats.Args, GetCategoryStats.Result, GetCategoryStats>(new(category))!;

	public List<GetCategoryEventTypes.Result> GetCategoryEventTypes(string category)
		=> string.IsNullOrWhiteSpace(category)
			? []
			: db.QueryToList<GetCategoryEventTypes.Args, GetCategoryEventTypes.Result, GetCategoryEventTypes>(new(category));

	public List<GetExplicitTransactions.Result> GetExplicitTransactions()
		=> db.QueryToList<GetExplicitTransactions.Result, GetExplicitTransactions>();

	public List<GetLongestStreams.Result> GetLongestStreams()
		=> db.QueryToList<GetLongestStreams.Result, GetLongestStreams>();
}
