// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests;

/// <summary>
/// Compact chain assembly for tests exercising doubles, where the score scale is not the thing
/// under test — everything runs on <see cref="NativeScale"/>. Tests that pin scale behavior
/// compose their chains inline with real scales instead.
/// </summary>
static class Chains {
	public static KontextRetriever Retriever(ICandidateFuser fuser, IReadOnlyList<ISearch> searches, params IReadOnlyList<IStep<Pool<NativeScale>, Pool<NativeScale>>> stages) =>
		Retriever(null, fuser, searches, stages);

	public static KontextRetriever Retriever(IQueryPlanner? planner, ICandidateFuser fuser, IReadOnlyList<ISearch> searches, params IReadOnlyList<IStep<Pool<NativeScale>, Pool<NativeScale>>> stages) {
		var pool = (planner is null ? PlanStep.Default() : new PlanStep(planner))
			.Then(new SearchStep(searches))
			.Then(new FuseStep<NativeScale>(fuser));

		foreach (var stage in stages)
			pool = pool.Then(stage);

		return KontextRetriever.From("test", pool.Then(new CutStep<NativeScale>()));
	}
}
