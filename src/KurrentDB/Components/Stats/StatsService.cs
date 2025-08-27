// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dapper;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.Components.Stats;

internal class StatsService(DuckDBConnectionPool db) {
	public IEnumerable<CategoryName> GetCategories() {
		using var connection = db.Open();
		var st = new PreparedStatement(connection, StatsSql.GetAllCategories.CommandText);
		foreach (var row in new QueryResult<CategoryName, StatsSql.GetAllCategories>(st)) {
			yield return row;
		}
	}

	public IEnumerable<(long, long)> GetCategoryStats(string category) {
		if (string.IsNullOrWhiteSpace(category)) return [];
		using var connection = db.Open();
		return connection.Query<(long, long)>("select count(distinct stream), count(rowid) from idx_all where category = $category", new { category });
	}

	public IEnumerable<CategoryEventTypes> GetCategoryEventTypes(string category) {
		if (string.IsNullOrWhiteSpace(category)) return [];
		using var connection = db.Open();
		return connection.Query<CategoryEventTypes>(CategoryEventTypesSql, new { category });
	}

	public CombinedStats[] GetStats() {
		using var connection = db.Open();
		var categories = connection.Query<CategoryStats>(CategoriesSql);
		var eventTypes = connection.Query<CategoryEventTypeStats>(CategoriesEventTypesSql);
		return categories.GroupJoin(eventTypes, x => x.category, y => y.category, (x, y) => new CombinedStats(x, y.ToArray())).ToArray();
	}

	private const string CategoriesSql =
		"""
		select
			category,
			count(distinct stream) as num_streams,
			count(log_position) AS num_events
		from idx_all
		group by category
		""";

	private const string CategoriesEventTypesSql =
		"""
		select
			category,
			event_type,
			count(log_position) AS num_events,
			epoch_ms(min(created)) as first_added,
			epoch_ms(max(created)) as last_added
		from idx_all
		group by category, event_type
		""";

	private const string CategoryEventTypesSql =
		"""
		select
			event_type as EventType,
			count(rowid) AS NumEvents,
			epoch_ms(min(created)) as FirstAdded,
			epoch_ms(max(created)) as LastAdded
		from idx_all
		where category = $category
		group by event_type
		""";

	public record CategoryEventTypes {
		public string EventType { get; init; }
		public long NumEvents { get; init; }
		public DateTime FirstAdded { get; init; }
		public DateTime LastAdded { get; init; }
	}

	public record CategoryStats {
		public string category { get; init; }
		public long num_streams { get; init; }
		public long num_events { get; init; }
	}

	public record CategoryEventTypeStats {
		public string category { get; init; }
		public string event_type { get; init; }
		public long num_events { get; init; }
		public DateTime first_added { get; init; }
		public DateTime last_added { get; init; }
	}
}

internal record CombinedStats(StatsService.CategoryStats Category, StatsService.CategoryEventTypeStats[] EventType) {
	public long AvgStreamLength => Category.num_events / Category.num_streams;
}
