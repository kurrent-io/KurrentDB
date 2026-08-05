// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable LoopCanBeConvertedToQuery

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Runs the pipeline: plan → every search in parallel → fuse → stage chain in order → cut.
/// <para>The searches, fuser, and stages are the variability. This class is deliberately just the wiring.</para>
/// <para>A failing search fails the retrieval — with no trace channel to report a degraded leg,
/// failing loudly beats silently returning partial recall.</para>
/// </summary>
public sealed class KontextRetriever(
	IQueryPlanner planner,
	IReadOnlyList<ISearch> searches,
	ICandidateFuser fuser,
	IReadOnlyList<IRetrievalStage> stages
) : IKontextRetriever {
	readonly IReadOnlyList<ISearch> _searches = searches.Count > 0
		? searches
		: throw new ArgumentException(@"A retrieval pipeline needs at least one search.", nameof(searches));

	/// <summary>Starts a pipeline over the default planner: default overfetch policy, wall clock.</summary>
	public static KontextRetrieverBuilder New() =>
		new(new DefaultQueryPlanner(new OverfetchOptions(), TimeProvider.System));

	public async ValueTask<IReadOnlyList<ScoredMemory>> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default) {
		// Plan
		var planned = await planner
			.PlanAsync(query, ct)
			.ConfigureAwait(false);

		// Search
		var sets = await Task
			.WhenAll(_searches.Select(search => search.SearchAsync(planned, ct).AsTask()))
			.ConfigureAwait(false);

		// Fuse
		var pool = fuser.Fuse(sets, planned);

		// Stages
		foreach (var stage in stages)
			pool = await stage
				.ProcessAsync(planned, pool, ct)
				.ConfigureAwait(false);

		// Cut
		return pool
			.Where(memory => memory.Score >= query.MinScore)
			.Take(query.Limit)
			.ToList();
	}
}
