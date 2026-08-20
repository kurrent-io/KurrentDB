// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The knobs of the tunable chains (<c>Default</c>, <c>Hybrid</c>, <c>Legacy</c>). The shipped
/// <c>Focused</c> chain is pinned to its measured optimum and reads none of these — a host that
/// wants a tuned pipeline registers its own <c>IKontextRetriever</c>, which beats the default.
/// </summary>
public sealed class KontextRetrievalOptions {
    /// <summary>How far past the requested limit the searches over-fetch before fusion.</summary>
    public OverfetchOptions Overfetch { get; set; } = new();

    /// <summary>The hybrid chain's engine blend: 0 = pure keyword, 1 = pure vector. Only the hybrid chain reads it.</summary>
    public double Alpha { get; set; } = 0.5;

    /// <summary>The pool-local BM25 reread that refines the fused order before modulation.</summary>
    public Bm25RerankerOptions Reranking { get; set; } = new();

    /// <summary>The cognitive scoring weights: recency, importance, and relevance.</summary>
    public CognitiveModulationOptions Modulation { get; set; } = new();

    /// <summary>The MMR diversity trade-off applied at the end of the chain.</summary>
    public MmrReordererOptions Reordering { get; set; } = new();

    /// <summary>The clock the planner ages candidates against. A registered <see cref="TimeProvider"/> overrides it.</summary>
    public TimeProvider? Time { get; set; }
}
