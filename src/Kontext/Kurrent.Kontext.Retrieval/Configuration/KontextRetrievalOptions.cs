// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The default pipeline's knobs, gathered so a host can tune the standard chain without
/// rebuilding it: register an instance before <c>AddKontext</c> and the default pipeline picks it
/// up. Hosts composing their own pipeline (via the <c>AddKontextRetrieval</c> configure hook) own
/// their options instead.
/// </summary>
public sealed class KontextRetrievalOptions {
    /// <summary>How far past the requested limit the searches over-fetch before fusion.</summary>
    public OverfetchOptions Overfetch { get; set; } = new();

    /// <summary>The pool-local BM25 reread that refines the fused order before modulation.</summary>
    public Bm25RerankerOptions Reranking { get; set; } = new();

    /// <summary>The cognitive scoring weights: recency, importance, relevance, and certainty.</summary>
    public CognitiveModulationOptions Modulation { get; set; } = new();

    /// <summary>The MMR diversity trade-off applied at the end of the chain.</summary>
    public MmrReordererOptions Reordering { get; set; } = new();

    /// <summary>The clock the planner ages candidates against. A registered <see cref="TimeProvider"/> overrides it.</summary>
    public TimeProvider? Time { get; set; }
}
