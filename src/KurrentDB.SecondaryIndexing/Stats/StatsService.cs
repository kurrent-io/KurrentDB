// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Stats.StatsSql;

namespace KurrentDB.SecondaryIndexing.Stats;

public class StatsService(DuckDBConnectionPool db) {
	public IEnumerable<CategoryName> GetCategories() {
		using var connection = db.Open();
		var st = new PreparedStatement(connection, GetAllCategories.CommandText);
		foreach (var row in new QueryResult<CategoryName, GetAllCategories>(st)) {
			yield return row;
		}
	}

	public (long StreamCount, long EventCount) GetTotalStats() {
		var result = db.QueryFirstOrDefault<GetTotalStats.Result, GetTotalStats>();
		return result == null ? (0, 0) : (result.Value.StreamCount, result.Value.EventCount);
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
