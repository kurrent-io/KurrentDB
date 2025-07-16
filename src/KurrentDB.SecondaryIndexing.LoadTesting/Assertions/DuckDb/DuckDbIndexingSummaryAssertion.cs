// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Observability;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

public class DuckDbIndexingSummaryAssertion(DuckDbDataSource db): IIndexingSummaryAssertion {
	public async ValueTask IndexesMatch(IndexingSummary summary) {
		await AssertCategoriesAreIndexed(summary);
		await AssertEventTypesAreIndexed(summary);
	}

	private ValueTask AssertCategoriesAreIndexed(IndexingSummary summary) {
		var categories = db.Pool.Query<CategorySummary, GetCategoriesSummaryQuery>();

		if (categories.All(c => summary.Categories.ContainsKey(c.Name)))
			return ValueTask.FromException(new Exception("Categories doesn't match;"));

		return ValueTask.CompletedTask;
	}

	private ValueTask AssertEventTypesAreIndexed(IndexingSummary summary) {
		var eventTypes = db.Pool.Query<EventTypeSummary, GetEventTypesSummaryQuery>();

		if (eventTypes.All(c => summary.EventTypes.ContainsKey(c.Name)))
			return ValueTask.FromException(new Exception("Event Types doesn't match;"));

		return ValueTask.CompletedTask;
	}
}
