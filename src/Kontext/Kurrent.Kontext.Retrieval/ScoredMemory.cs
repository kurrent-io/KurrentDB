// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>One ranked memory with its full score breakdown.</summary>
public sealed record ScoredMemory {
    public required MemoryContracts.StoredMemory Memory { get; init; }

    /// <summary>The value the ranking ordered on — higher = stronger. Semantics depend on the pipeline (see the breakdown).</summary>
    public required double Score { get; init; }

    public required ScoreBreakdown Breakdown { get; init; }
}

/// <summary>
/// Every number that fed a memory's final score, kept nullable so a stage that did not run never
/// fabricates a value. Enough to reproduce the ranking from the log alone.
/// </summary>
public sealed record ScoreBreakdown {
    /// <summary>The fusion (RRF / additive) score.</summary>
    public required double Fused { get; init; }

    /// <summary>The relevance model's score; null if no model ran.</summary>
    public double? Reranked { get; init; }

    /// <summary>This memory's 1-based rank in each source that surfaced it.</summary>
    public required IReadOnlyDictionary<string, int> SourceRanks { get; init; }

    /// <summary>This memory's source-native score in each source that surfaced it (orientation: higher = better).</summary>
    public required IReadOnlyDictionary<string, double> SourceScores { get; init; }

    public double? RelevanceRaw { get; init; }

    public double? RelevanceNorm { get; init; }

    public double? RecencyRaw { get; init; }

    public double? RecencyNorm { get; init; }

    public double? ImportanceRaw { get; init; }

    public double? ImportanceNorm { get; init; }

    /// <summary>The alpha-weighted sum of the normalized dimensions — the modulated score.</summary>
    public double? BaseScore { get; init; }

    /// <summary>The reordering stage's own score (e.g. the MMR value) when it reordered the list.</summary>
    public double? ReorderScore { get; init; }
}
