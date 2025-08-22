// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using Dapper;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.Services;

class StatsService(DuckDBConnectionPool db) {
	public CombinedStats[] GetStats() {
		using var connection = db.Open();
		var categories = connection.Query<CategoryStats>(CategoriesSql);
		var eventTypes = connection.Query<CategoryEventTypeStats>(CategoriesEventTypesSql);
		return categories.GroupJoin(eventTypes, x => x.category, y => y.category, (x, y) => new CombinedStats(x, y.ToArray())).ToArray();
	}

	const string CategoriesSql =
		"""
		select
			category,
			count(distinct stream) as num_streams,
			count(log_position) AS num_events
		from idx_all
		group by category
		""";

	const string CategoriesEventTypesSql =
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

	const string LongestStreamSql =
		"""
		SELECT s.name, COUNT(idx_all.log_position) AS num_events
		FROM streams s JOIN idx_all ON s.id = idx_all.stream_id
		WHERE idx_all.category_id=@category
		GROUP BY s.name
		ORDER BY num_events DESC LIMIT 1
		""";

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

record CombinedStats(StatsService.CategoryStats Category, StatsService.CategoryEventTypeStats[] EventType) {
	public long AvgStreamLength => Category.num_events / Category.num_streams;
}
