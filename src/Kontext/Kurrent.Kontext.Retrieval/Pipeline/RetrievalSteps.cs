// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Pipelines;

namespace Kurrent.Kontext.Retrieval;

/// <summary>Shapes the query before the searches run: overfetch, expansion, the pinned clock.</summary>
public sealed class PlanStep(IQueryPlanner planner) : IStep<RetrievalQuery, Planned> {
    public static PlanStep Default() =>
        new(new DefaultQueryPlanner(new OverfetchOptions()));

    public async ValueTask<Planned> Execute(RetrievalQuery input, CancellationToken ct) =>
        new(input, await planner.PlanAsync(input, ct).ConfigureAwait(false));
}

/// <summary>
/// Runs every search leg in parallel over the planned query. A failing leg fails the retrieval —
/// with no trace channel to report a degraded leg, failing loudly beats silently returning
/// partial recall.
/// </summary>
public sealed class SearchStep : IStep<Planned, Searched> {
    readonly IStep<Planned, Searched> _legs;

    public SearchStep(params IReadOnlyList<ISearch> searches) {
        if (searches.Count == 0)
            throw new ArgumentException(@"A retrieval pipeline needs at least one search.", nameof(searches));

        _legs = Steps.FanIn<Planned, CandidateSet, Searched>(
            [.. searches.Select(AsStep)],
            static (sets, planned) => new(planned, sets));
    }

    public ValueTask<Searched> Execute(Planned input, CancellationToken ct) =>
        _legs.Execute(input, ct);

    static IStep<Planned, CandidateSet> AsStep(ISearch search) =>
        Steps.Lambda<Planned, CandidateSet>((planned, ct) => search.SearchAsync(planned.Plan, ct));
}

/// <summary>
/// Merges the legs' ranked lists into one scored pool. The scale is the fuser's to produce and
/// this step's to declare — <typeparamref name="TScale"/> is the one declaration site the rest
/// of the chain is type-checked against.
/// </summary>
public sealed class FuseStep<TScale>(ICandidateFuser fuser) : IStep<Searched, Pool<TScale>> where TScale : IScoreScale {
    public ValueTask<Pool<TScale>> Execute(Searched input, CancellationToken ct) =>
        new(new Pool<TScale>(input.Query, fuser.Fuse(input.Sets, input.Query.Plan)));
}

/// <summary>
/// The final cut: drops memories under the caller's <see cref="RetrievalQuery.MinScore"/> and
/// takes the caller's <see cref="RetrievalQuery.Limit"/>. Typed to the chain's final scale so a
/// composition ending on a different scale fails to compile instead of silently mis-cutting.
/// </summary>
public sealed class CutStep<TScale> : IStep<Pool<TScale>, IReadOnlyList<ScoredMemory>> where TScale : IScoreScale {
    public ValueTask<IReadOnlyList<ScoredMemory>> Execute(Pool<TScale> input, CancellationToken ct) {
        var query = input.Query.Source;

        return new(input.Memories
            .Where(memory => memory.Score >= query.MinScore)
            .Take(query.Limit)
            .ToList());
    }
}
