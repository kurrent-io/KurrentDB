// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
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
}
