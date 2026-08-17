// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Pipelines;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// A named retrieval chain: plan → search fan-out → fuse → typed stage links → cut, composed with
/// <see cref="Step.Then{TIn,TMid,TOut}(IStep{TIn,TMid},IStep{TMid,TOut})"/> so the carrier types
/// enforce the phase order and the pool's score scale at compile time. The shipped shapes are the
/// static factories below — call them directly when the variant is known at compile time; only
/// runtime selection (host config, benchmark sweeps) goes through <see cref="RetrieverCatalogue"/>.
/// </summary>
public sealed class KontextRetriever : IKontextRetriever {
    readonly IStep<RetrievalQuery, IReadOnlyList<ScoredMemory>> _chain;

    KontextRetriever(string variant, IStep<RetrievalQuery, IReadOnlyList<ScoredMemory>> chain) {
        Variant = variant;
        _chain  = chain;
    }

    public string Variant { get; }

    /// <summary>Names a composed chain — the name is what benchmarks and logs attribute rankings to.</summary>
    public static KontextRetriever From(string variant, IStep<RetrievalQuery, IReadOnlyList<ScoredMemory>> chain) =>
        new(variant, chain);

    public ValueTask<IReadOnlyList<ScoredMemory>> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default) =>
        _chain.Execute(query, ct);

    /// <summary>The default chain: both legs, rank fusion, BM25 reread, modulation, the entity nudge, MMR, then the seat caps.</summary>
    public static KontextRetriever Default(RetrieverParts parts) {
        var (index, embeddings, options) = parts;

        var planner = new DefaultQueryPlanner(options.Overfetch, options.Time);

        return From(RetrieverVariants.Default,
            new PlanStep(planner)
                .Then(new SearchStep(new VectorSearch(index, embeddings), new KeywordSearch(index)))
                .Then(new FuseStep<RrfScale>(ReciprocalRankFuser.Create()))
                .Then(Bm25Reranker<RrfScale>.Create(options.Reranking))
                .Then(CognitiveModulator<RrfScale>.Create(options.Modulation))
                .Then(EntityModulator<UnitScale>.Create(parts.Entities, options.Entities))
                .Then(MmrReorderer<UnitScale>.Create(options.Reordering))
                .Then(SeatAllocator<UnitScale>.Create(options.Seats))
                .Then(new CutStep<UnitScale>()));
    }

    /// <summary>
    /// The shipped chain: the engine's alpha blend, BM25 reread, modulation, and the entity nudge
    /// — no MMR. Deliberately not configurable: the knobs are the 2026-08-15 LoCoMo hill-climb
    /// optimum (recall@5 0.4889 vs 0.4622 for hybrid α 0.5; the diversity reorder was costing
    /// recall), and a tuned variant is a different chain — compose it from the steps directly.
    /// The entity nudge falls through when the host has no entity index, leaving exactly the
    /// measured chain.
    /// </summary>
    public static KontextRetriever Focused(RetrieverParts parts) {
        const double measuredAlpha = 0.45;

        var (index, embeddings, options) = parts;

        var planner = new DefaultQueryPlanner(new OverfetchOptions(), options.Time);

        return From(RetrieverVariants.Focused,
            new PlanStep(planner)
                .Then(new SearchStep(new HybridSearch(index, embeddings, measuredAlpha)))
                .Then(new FuseStep<NativeScale>(new IdentityFuser()))
                .Then(Bm25Reranker<NativeScale>.Create())
                .Then(CognitiveModulator<RrfScale>.Create())
                .Then(EntityModulator<UnitScale>.Create(parts.Entities))
                .Then(new CutStep<UnitScale>()));
    }

    /// <summary>
    /// The hybrid-comparison chain: Lance's in-engine alpha blend as the single search leg, then
    /// the same reread, modulation, MMR, and seat stages as the default chain — so a benchmark
    /// isolates the fusion step: engine alpha blend versus Kontext rank fusion.
    /// </summary>
    public static KontextRetriever Hybrid(RetrieverParts parts) {
        var (index, embeddings, options) = parts;

        var planner = new DefaultQueryPlanner(options.Overfetch, options.Time);

        return From(RetrieverVariants.Hybrid,
            new PlanStep(planner)
                .Then(new SearchStep(new HybridSearch(index, embeddings, options.Alpha)))
                .Then(new FuseStep<NativeScale>(new IdentityFuser()))
                .Then(Bm25Reranker<NativeScale>.Create(options.Reranking))
                .Then(CognitiveModulator<RrfScale>.Create(options.Modulation))
                .Then(EntityModulator<UnitScale>.Create(parts.Entities, options.Entities))
                .Then(MmrReorderer<UnitScale>.Create(options.Reordering))
                .Then(SeatAllocator<UnitScale>.Create(options.Seats))
                .Then(new CutStep<UnitScale>()));
    }

    /// <summary>
    /// The legacy chain, kept as the baseline the default is measured against: a fixed candidate
    /// floor, normalized fusion, and no BM25 reread, modulation, or entity nudge — a frozen
    /// baseline stays frozen, or it stops being one.
    /// </summary>
    public static KontextRetriever Legacy(RetrieverParts parts) {
        const int retrievalCandidates = 30;

        var (index, embeddings, options) = parts;

        var planner = new DefaultQueryPlanner(new OverfetchOptions { Factor = 0, Floor = retrievalCandidates }, options.Time);

        return From(RetrieverVariants.Legacy,
            new PlanStep(planner)
                .Then(new SearchStep(new VectorSearch(index, embeddings), new KeywordSearch(index)))
                .Then(new FuseStep<UnitScale>(ReciprocalRankFuser.Create(static fusion => fusion.Normalize = true)))
                .Then(MmrReorderer<UnitScale>.Create())
                .Then(new CutStep<UnitScale>()));
    }
}
