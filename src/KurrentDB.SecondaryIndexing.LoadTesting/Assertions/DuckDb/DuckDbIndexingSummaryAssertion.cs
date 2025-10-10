// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Dapper;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.Tests.Observability;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

public class DuckDbIndexingSummaryAssertion(DuckDBConnectionPool db) : IIndexingSummaryAssertion {
	public async ValueTask IndexesMatch(IndexingSummary summary) {
		await AssertCategoriesAreIndexed(summary);
		await AssertEventTypesAreIndexed(summary);
	}

	private ValueTask AssertCategoriesAreIndexed(IndexingSummary summary) {
		using var connection = db.Open();
		var categories = connection.Query<string>("select distinct category from idx_all");

		return categories.All(c => summary.Categories.ContainsKey(c))
			? ValueTask.FromException(new Exception("Categories doesn't match;"))
			: ValueTask.CompletedTask;
	}

	private ValueTask AssertEventTypesAreIndexed(IndexingSummary summary) {
		using var connection = db.Open();
		var eventTypes = connection.Query<string>("select distinct event_type from idx_all");

		return eventTypes.All(c => summary.EventTypes.ContainsKey(c))
			? ValueTask.FromException(new Exception("Event Types doesn't match;"))
			: ValueTask.CompletedTask;
	}
}
