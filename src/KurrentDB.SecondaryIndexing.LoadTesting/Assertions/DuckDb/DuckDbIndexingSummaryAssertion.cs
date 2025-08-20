// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Observability;
using Dapper;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

public class DuckDbIndexingSummaryAssertion(DuckDbDataSource db): IIndexingSummaryAssertion {
	public async ValueTask IndexesMatch(IndexingSummary summary) {
		await AssertCategoriesAreIndexed(summary);
		await AssertEventTypesAreIndexed(summary);
	}

	private ValueTask AssertCategoriesAreIndexed(IndexingSummary summary) {
		using var connection = db.OpenNewConnection();
		var categories = connection.Query<string>("select distinct category from idx_all");

		return categories.All(c => summary.Categories.ContainsKey(c))
			? ValueTask.FromException(new Exception("Categories doesn't match;"))
			: ValueTask.CompletedTask;
	}

	private ValueTask AssertEventTypesAreIndexed(IndexingSummary summary) {
		using var connection = db.OpenNewConnection();
		var eventTypes = connection.Query<string>("select distinct event_type from idx_all");

		return eventTypes.All(c => summary.EventTypes.ContainsKey(c))
			? ValueTask.FromException(new Exception("Event Types doesn't match;"))
			: ValueTask.CompletedTask;
	}
}
