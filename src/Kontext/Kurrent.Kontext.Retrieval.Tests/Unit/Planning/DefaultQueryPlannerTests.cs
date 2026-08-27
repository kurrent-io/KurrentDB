// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Retrieval.Tests.Planning;

[Category("Planning")]
public class DefaultQueryPlannerTests {
	[Test]
	public async ValueTask caller_supplied_as_of_is_honored_and_the_clock_is_not_consulted() {
		var pinned  = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
		var planner = new DefaultQueryPlanner(new OverfetchOptions(), new ThrowingTimeProvider());

		var planned = await planner.PlanAsync(new RetrievalQuery { Text = "query", AsOf = pinned });

		// ThrowingTimeProvider would fail this test the moment GetUtcNow() is called at all —
		// `query.AsOf ?? time.GetUtcNow()` must short-circuit and never touch the clock.
		await Assert.That(planned.AsOf).IsEqualTo(pinned);
	}

	[Test]
	public async ValueTask null_as_of_stamps_the_injected_clocks_current_time() {
		var now     = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
		var planner = new DefaultQueryPlanner(new OverfetchOptions(), new FixedTimeProvider(now));

		var planned = await planner.PlanAsync(new RetrievalQuery { Text = "query", AsOf = null });

		await Assert.That(planned.AsOf).IsEqualTo(now);
	}

	[Test]
	public async ValueTask text_tags_and_limit_pass_through_unchanged() {
		var tags    = new List<MemoryContracts.Tag> { new() { Scope = "project", Value = "rivers" } };
		var planner = new DefaultQueryPlanner(new OverfetchOptions(), new FixedTimeProvider(Fixtures.Now));

		var query   = new RetrievalQuery { Text = "what did we decide about rivers", Tags = tags, Limit = 7 };
		var planned = await planner.PlanAsync(query);

		await Assert.That(planned.Text).IsEqualTo("what did we decide about rivers");
		await Assert.That(planned.Tags).IsEquivalentTo(tags, CollectionOrdering.Matching);
		await Assert.That(planned.Limit).IsEqualTo(7);
	}

	[Test]
	public async ValueTask pool_size_comes_from_the_injected_overfetch_options_not_from_limit() {
		// Factor=1, Floor=999 makes limit*Factor (10) and PoolSize (999) impossible to confuse —
		// a planner that wired PoolSize = Limit would fail this immediately.
		var overfetch = new OverfetchOptions { Factor = 1, Floor = 999 };
		var planner   = new DefaultQueryPlanner(overfetch, new FixedTimeProvider(Fixtures.Now));

		var planned = await planner.PlanAsync(new RetrievalQuery { Text = "query", Limit = 10 });

		await Assert.That(planned.PoolSize).IsEqualTo(999);
	}
}

sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
	public override DateTimeOffset GetUtcNow() => now;
}

sealed class ThrowingTimeProvider : TimeProvider {
	public override DateTimeOffset GetUtcNow() =>
		throw new InvalidOperationException("The clock must not be consulted when the caller pins AsOf.");
}
