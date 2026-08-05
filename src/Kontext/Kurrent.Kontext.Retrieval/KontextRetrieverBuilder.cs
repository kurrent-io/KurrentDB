// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Accumulates searches, fuser, and stages, then snapshots them into a <see cref="KontextRetriever"/>.
/// Obtained from <see cref="KontextRetriever.New"/>.
/// <para>Fusion has a default: one search gets <see cref="IdentityFuser"/>, several get
/// <see cref="ReciprocalRankFuser"/>. Call <see cref="Fuser"/> to override.</para>
/// </summary>
[PublicAPI]
public sealed class KontextRetrieverBuilder {
    readonly List<ISearch>         _searches = [];
    readonly List<IRetrievalStage> _stages   = [];

    IQueryPlanner    _planner;
    ICandidateFuser? _fuser;

    internal KontextRetrieverBuilder(IQueryPlanner planner) =>
        _planner = planner;

    /// <summary>
    /// Sets the query planner for the pipeline.
    /// </summary>
    /// <param name="planner">The planner that shapes the query before the searches run: overfetch tuning, a pinned clock, or a query-expanding decorator.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public KontextRetrieverBuilder Planner(IQueryPlanner planner) {
        _planner = planner;
        return this;
    }

    /// <summary>
    /// Sets the default planner over the given overfetch policy — the shorthand for the common case.
    /// </summary>
    /// <param name="overfetch">How far past the requested limit the searches over-fetch.</param>
    /// <param name="time">The clock the planner stamps <see cref="PlannedQuery.AsOf"/> from; <see cref="TimeProvider.System"/> when null. Pin it for reproducible rankings.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public KontextRetrieverBuilder Planner(OverfetchOptions overfetch, TimeProvider? time = null) =>
        Planner(new DefaultQueryPlanner(overfetch, time ?? TimeProvider.System));

    /// <summary>
    /// Adds a search whose results feed the fused candidate pool. Searches run in parallel.
    /// </summary>
    /// <param name="search">The search to add.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public KontextRetrieverBuilder AddSearch(ISearch search) {
        _searches.Add(search);
        return this;
    }

    /// <summary>
    /// Sets how the searches' ranked lists merge into one pool, overriding the default fusion.
    /// </summary>
    /// <param name="fuser">The fuser that merges the candidate sets.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public KontextRetrieverBuilder Fuser(ICandidateFuser fuser) {
        _fuser = fuser;
        return this;
    }

    /// <summary>
    /// Appends a stage to the chain. Stages run in the order they are added.
    /// </summary>
    /// <param name="stage">The stage to append to the chain.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public KontextRetrieverBuilder AddStage(IRetrievalStage stage) {
        _stages.Add(stage);
        return this;
    }

    /// <summary>
    /// Builds the retriever from the accumulated searches, fuser, and stages.
    /// </summary>
    /// <remarks>
    /// The searches and stage chain are snapshotted: builder calls after this point never affect
    /// an already-built retriever.
    /// </remarks>
    /// <returns>The configured <see cref="KontextRetriever"/>.</returns>
    public KontextRetriever Build() =>
        new(_planner, [.. _searches], _fuser ?? DefaultFuser(), [.. _stages]);

    ICandidateFuser DefaultFuser() =>
        _searches.Count > 1 ? ReciprocalRankFuser.Create() : new IdentityFuser();
}
