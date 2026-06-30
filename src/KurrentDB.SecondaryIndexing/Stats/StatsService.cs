// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.DependencyInjection;
using static KurrentDB.SecondaryIndexing.Stats.StatsSql;

namespace KurrentDB.SecondaryIndexing.Stats;

public class StatsService(IServiceProvider services) {
	private readonly DuckDBConnectionPool _pool = services.GetRequiredService<DuckDBConnectionPool>();
	private readonly DefaultIndexProcessor _defaultIndex = services.GetRequiredService<DefaultIndexProcessor>();

	public IEnumerable<CategoryName> GetCategories() {
		using var connection = _pool.Open();
		using var snapshot = _defaultIndex.CaptureSnapshot(connection);
		using var statement = new PreparedStatement(connection, GetAllCategories.CommandText);
		foreach (var row in new QueryResult<CategoryName, GetAllCategories>(statement)) {
			yield return row;
		}
	}

	// note: very expensive. count(distinct stream) over idx_all filtered by category
	public IReadOnlyList<GetCategoryStats.Result> GetCategoryStats(string category) {
		if (string.IsNullOrWhiteSpace(category))
			return [];

		var result = new List<GetCategoryStats.Result>(100);
		using var connection = _pool.Open();
		using var snapshot = _defaultIndex.CaptureSnapshot(connection);
		connection
			.ExecuteQuery<GetCategoryStats.Args, GetCategoryStats.Result, GetCategoryStats>(new(category))
			.CopyTo(result);

		return result;
	}

	// note: expensive. group by event_type over idx_all filtered by category
	public IReadOnlyList<GetCategoryEventTypes.Result> GetCategoryEventTypes(string category) {
		if (string.IsNullOrWhiteSpace(category))
			return [];

		var result = new List<GetCategoryEventTypes.Result>(100);
		using var connection = _pool.Open();
		using var snapshot = _defaultIndex.CaptureSnapshot(connection);

		connection
			.ExecuteQuery<GetCategoryEventTypes.Args, GetCategoryEventTypes.Result, GetCategoryEventTypes>(new(category))
			.CopyTo(result);

		return result;
	}

	// note: very expensive. count(distinct commit_position) grouped by category over idx_all
	public IReadOnlyList<GetExplicitTransactions.Result> GetExplicitTransactions() {
		var result = new List<GetExplicitTransactions.Result>(100);
		using var connection = _pool.Open();
		using var snapshot = _defaultIndex.CaptureSnapshot(connection);

		connection
			.ExecuteQuery<GetExplicitTransactions.Result, GetExplicitTransactions>()
			.CopyTo(result);

		return result;
	}

	// note: expensive. DISTINCT ON(category) with ORDER BY event_number DESC over idx_all
	public List<GetLongestStreams.Result> GetLongestStreams() {
		var result = new List<GetLongestStreams.Result>(100);
		using var connection = _pool.Open();
		using var snapshot = _defaultIndex.CaptureSnapshot(connection);

		connection
			.ExecuteQuery<GetLongestStreams.Result, GetLongestStreams>()
			.CopyTo(result);

		return result;
	}
}
