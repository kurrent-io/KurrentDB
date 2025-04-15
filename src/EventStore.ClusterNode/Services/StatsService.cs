// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using Dapper;
using DuckDB.NET.Data;
using EventStore.Core.Duck;

namespace EventStore.ClusterNode.Services;

class StatsService(DuckDb db) {
	public CombinedStats[] GetStats() {
		var connection = db.GetOrOpenConnection();
		var categories = connection.Query<CategoryStats>(CategoriesSql);
		var eventTypes = connection.Query<CategoryEventTypeStats>(CategoriesEventTypesSql);
		db.ReturnConnection(connection);
		return categories.GroupJoin(eventTypes, x => x.CategoryId, y => y.CategoryId, (x, y) => new CombinedStats(x, y.ToArray())).ToArray();
	}

	const string CategoriesSql =
		"""
		SELECT
			c.id as CategoryId, c.name AS CategoryName,
			COUNT(DISTINCT s.id) as NumStreams, COUNT(DISTINCT i.seq) AS NumEvents
		FROM category c
			JOIN idx_all i ON c.id = i.category
			JOIN streams s ON i.stream = s.id
		GROUP BY c.id, c.name
		""";

	const string CategoriesEventTypesSql =
		"""
		SELECT
			c.id as CategoryId, e.id as EventTypeId,
			e.name AS EventType, COUNT(DISTINCT i.seq) AS NumEvents,
			min(i.created) as FirstAdded, max(i.created) as LastAdded
		FROM category c
			JOIN idx_all i ON c.id = i.category
			JOIN event_type e ON i.event_type = e.id
			JOIN streams s ON i.stream = s.id
		GROUP BY c.id, e.id, e.name
		""";

	const string LongestStreamSql =
		"""
		SELECT s.name, COUNT(idx_all.seq) AS num_events
		FROM streams s JOIN idx_all ON s.id = idx_all.stream
		WHERE idx_all.category=@category
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
		public DateTime FirstAdded { get; init; }
		public DateTime LastAdded { get; init; }
	}
}

record CombinedStats(StatsService.CategoryStats Category, StatsService.CategoryEventTypeStats[] EventType) {
	public long AvgStreamLength => Category.NumEvents / Category.NumStreams;
}
