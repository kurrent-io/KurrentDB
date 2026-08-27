// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Retrieval.Tests.Planning;

[Category("Planning")]
public class QueryExpandingQueryPlannerTests {
	[Test]
	public async ValueTask inner_planner_receives_the_expanded_text() {
		var inner   = new RecordingQueryPlanner();
		var planner = new QueryExpandingQueryPlanner(new FixedQueryExpander("expanded text"), inner);

		await planner.PlanAsync(new RetrievalQuery { Text = "original text" });

		await Assert.That(inner.Received!.Text).IsEqualTo("expanded text");
	}

	[Test]
	public async ValueTask every_other_field_survives_the_decorator_untouched() {
		var pinned = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var tags   = new List<MemoryContracts.Tag> { new() { Scope = "project", Value = "rivers" } };

		var inner   = new RecordingQueryPlanner();
		var planner = new QueryExpandingQueryPlanner(new FixedQueryExpander("expanded"), inner);

		var query = new RetrievalQuery { Text = "original", Tags = tags, Limit = 7, AsOf = pinned };
		await planner.PlanAsync(query);

		// "Rewrite only the text; every other field is the caller's intent" — tags, limit, and the
		// caller's pinned clock must reach the inner planner exactly as given.
		await Assert.That(inner.Received!.Tags).IsEquivalentTo(tags, CollectionOrdering.Matching);
		await Assert.That(inner.Received!.Limit).IsEqualTo(7);
		await Assert.That(inner.Received!.AsOf).IsEqualTo(pinned);
	}
}

sealed class FixedQueryExpander(string expanded) : IQueryExpander {
	public ValueTask<string> ExpandAsync(string query, CancellationToken ct = default) =>
		ValueTask.FromResult(expanded);
}

sealed class RecordingQueryPlanner : IQueryPlanner {
	public RetrievalQuery? Received { get; private set; }

	public ValueTask<PlannedQuery> PlanAsync(RetrievalQuery query, CancellationToken ct = default) {
		Received = query;

		return ValueTask.FromResult(new PlannedQuery {
			Text     = query.Text,
			Tags     = query.Tags,
			Limit    = query.Limit,
			PoolSize = 60,
			AsOf     = query.AsOf ?? Fixtures.Now,
		});
	}
}
