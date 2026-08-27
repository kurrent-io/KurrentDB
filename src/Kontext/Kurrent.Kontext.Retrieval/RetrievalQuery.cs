// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// What the caller wants back from memory: a natural-language question, optional tag scoping,
/// and the ranking cut.
/// </summary>
public sealed record RetrievalQuery {
    /// <summary>The question in natural language — both search legs consume it (the vector leg via its embedding).</summary>
    public required string Text { get; init; }

    /// <summary>Pre-filter: only memories carrying ALL of these tags enter the candidate pool. Empty = search everything.</summary>
    public IReadOnlyCollection<MemoryContracts.Tag> Tags { get; init; } = [];

    /// <summary>Max memories returned after ranking.</summary>
    public int Limit { get; init; } = 10;

    /// <summary>
    /// Memories scoring below this after the full pipeline are dropped. 0 = keep everything that
    /// ranked. The scale is pipeline-dependent — raw BM25 (keyword-only), RRF sums (~0.01–0.03), or
    /// [0,1] (cognitive/additive) — so never carry a nonzero cutoff across pipelines in a sweep.
    /// </summary>
    public double MinScore { get; init; }

    /// <summary>
    /// The "now" recency decays from. Null = the pipeline stamps the wall clock at plan time.
    /// Pin it for reproducible benchmarks.
    /// </summary>
    public DateTimeOffset? AsOf { get; init; }
}

/// <summary>
/// The query after planning: the (possibly expanded) query text, the candidate-pool size resolved
/// from the overfetch policy, and the recency clock pinned. Embedding is not a planning concern —
/// each source that ranks by meaning embeds this text itself, with the model that owns its store.
/// </summary>
public sealed record PlannedQuery {
    public required string Text { get; init; }

    public required IReadOnlyCollection<MemoryContracts.Tag> Tags { get; init; }

    public required int Limit { get; init; }

    /// <summary>How many candidates each source fetches before fusion — always over the limit so fusion has something to work with.</summary>
    public required int PoolSize { get; init; }

    /// <summary>The pinned "now" every recency computation in this retrieval decays from.</summary>
    public required DateTimeOffset AsOf { get; init; }
}
