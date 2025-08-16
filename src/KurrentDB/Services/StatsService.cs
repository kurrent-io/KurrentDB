// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Linq;
using Dapper;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.Services;

class StatsService(DuckDbDataSource dataSource) {
	public CombinedStats[] GetStats() {
		using var connection = dataSource.Pool.Open();
		var categories = connection.Query<CategoryStats>(CategoriesSql);
		var eventTypes = connection.Query<CategoryEventTypeStats>(CategoriesEventTypesSql);
		return categories.GroupJoin(eventTypes, x => x.CategoryId, y => y.CategoryId, (x, y) => new CombinedStats(x, y.ToArray())).ToArray();
	}

	const string CategoriesSql =
		"""
		SELECT
			c.id as CategoryId, c.name AS CategoryName,
			COUNT(DISTINCT s.id) as NumStreams, COUNT(DISTINCT i.log_position) AS NumEvents
		FROM categories c
			JOIN idx_all i ON c.id = i.category_id
			JOIN streams s ON i.stream_id = s.id
		GROUP BY c.id, c.name
		""";

	const string CategoriesEventTypesSql =
		"""
		SELECT
			c.id as CategoryId, e.id as EventTypeId,
			e.name AS EventType, COUNT(DISTINCT i.log_position) AS NumEvents,
			min(i.created) as FirstAdded, max(i.created) as LastAdded
		FROM categories c
			JOIN idx_all i ON c.id = i.category_id
			JOIN event_types e ON i.event_type_id = e.id
			JOIN streams s ON i.stream_id = s.id
		GROUP BY c.id, e.id, e.name
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
		public long CategoryId { get; init; }
		public string CategoryName { get; init; }
		public long NumStreams { get; init; }
		public long NumEvents { get; init; }
	}

	public record CategoryEventTypeStats {
		public long CategoryId { get; init; }
		public long EventTypeId { get; init; }
		public string EventType { get; init; }
		public int NumEvents { get; init; }
		public long FirstAdded { get; init; }
		public long LastAdded { get; init; }
	}
}

record CombinedStats(StatsService.CategoryStats Category, StatsService.CategoryEventTypeStats[] EventType) {
	public long AvgStreamLength => Category.NumEvents / Category.NumStreams;
}
