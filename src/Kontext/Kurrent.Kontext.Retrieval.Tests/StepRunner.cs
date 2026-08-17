// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests;

/// <summary>Runs a single pool-to-pool step over a bare pool, wrapping and unwrapping the carriers.</summary>
static class StepRunner {
	public static async ValueTask<IReadOnlyList<ScoredMemory>> Run<TIn, TOut>(
		this IStep<Pool<TIn>, Pool<TOut>> stage, IReadOnlyList<ScoredMemory> pool, PlannedQuery? query = null
	) where TIn : IScoreScale where TOut : IScoreScale {
		var plan   = query ?? Fixtures.Query();
		var result = await stage.Execute(new(new(new() { Text = plan.Text, Limit = plan.Limit }, plan), pool), CancellationToken.None);

		return result.Memories;
	}
}
