// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.DependencyInjection;
using static KurrentDB.SecondaryIndexing.Stats.StatsSql;

namespace KurrentDB.SecondaryIndexing.Stats;

public class StatsService(IServiceProvider services) {
	private readonly DuckDBExecutor _executor = services.GetRequiredService<DuckDBExecutor>();
	private readonly DefaultIndexProcessor _defaultIndex = services.GetRequiredService<DefaultIndexProcessor>();

	public async ValueTask<IReadOnlyList<CategoryName>> GetCategories(CancellationToken ct = default) {
		return await _executor.Execute(connection => {
			var result = new List<CategoryName>();
			using var snapshot = _defaultIndex.CaptureSnapshot(connection);
			using var statement = new PreparedStatement(connection, GetAllCategories.CommandText);
			foreach (var row in new QueryResult<CategoryName, GetAllCategories>(statement)) {
				result.Add(row);
			}

			return (IReadOnlyList<CategoryName>)result;
		}, ct);
	}

	// note: very expensive. count(distinct stream) over idx_all filtered by category
	public async ValueTask<IReadOnlyList<GetCategoryStats.Result>> GetCategoryStats(string category, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(category))
			return [];

		return await _executor.Execute(connection => {
			var result = new List<GetCategoryStats.Result>(100);
			using var snapshot = _defaultIndex.CaptureSnapshot(connection);
			connection
				.ExecuteQuery<GetCategoryStats.Args, GetCategoryStats.Result, GetCategoryStats>(new(category))
				.CopyTo(result);

			return (IReadOnlyList<GetCategoryStats.Result>)result;
		}, ct);
	}

	// note: expensive. group by event_type over idx_all filtered by category
	public async ValueTask<IReadOnlyList<GetCategoryEventTypes.Result>> GetCategoryEventTypes(string category, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(category))
			return [];

		return await _executor.Execute(connection => {
			var result = new List<GetCategoryEventTypes.Result>(100);
			using var snapshot = _defaultIndex.CaptureSnapshot(connection);
			connection
				.ExecuteQuery<GetCategoryEventTypes.Args, GetCategoryEventTypes.Result, GetCategoryEventTypes>(new(category))
				.CopyTo(result);

			return (IReadOnlyList<GetCategoryEventTypes.Result>)result;
		}, ct);
	}

	// note: very expensive. count(distinct commit_position) grouped by category over idx_all
	public async ValueTask<IReadOnlyList<GetExplicitTransactions.Result>> GetExplicitTransactions(CancellationToken ct = default) {
		return await _executor.Execute(connection => {
			var result = new List<GetExplicitTransactions.Result>(100);
			using var snapshot = _defaultIndex.CaptureSnapshot(connection);
			connection
				.ExecuteQuery<GetExplicitTransactions.Result, GetExplicitTransactions>()
				.CopyTo(result);

			return (IReadOnlyList<GetExplicitTransactions.Result>)result;
		}, ct);
	}

	// note: expensive. DISTINCT ON(category) with ORDER BY event_number DESC over idx_all
	public async ValueTask<List<GetLongestStreams.Result>> GetLongestStreams(CancellationToken ct = default) {
		return await _executor.Execute(connection => {
			var result = new List<GetLongestStreams.Result>(100);
			using var snapshot = _defaultIndex.CaptureSnapshot(connection);
			connection
				.ExecuteQuery<GetLongestStreams.Result, GetLongestStreams>()
				.CopyTo(result);

			return result;
		}, ct);
	}
}
