// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Fuses by rank alone: fused = Σ over sources of weight / (k + rank).
/// <para>Native scores are discarded, so BM25 and vector scales never need to be comparable.</para>
/// </summary>
public sealed class ReciprocalRankFuser(ReciprocalRankFusionOptions options) : ICandidateFuser {
    /// <summary>Creates the fuser from pre-built options — the config-binding door.</summary>
    public static ReciprocalRankFuser Create(ReciprocalRankFusionOptions options) =>
        new(options);

    /// <summary>Creates the fuser over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static ReciprocalRankFuser Create(Action<ReciprocalRankFusionOptions>? configure = null) {
        var options = new ReciprocalRankFusionOptions();
        configure?.Invoke(options);
        return Create(options);
    }

    public IReadOnlyList<ScoredMemory> Fuse(IReadOnlyList<CandidateSet> sets, PlannedQuery query) {
        var entries = FusionAccumulator.Collect(sets, (entry, set, _, rank) =>
            entry.Fused += options.WeightOf(set.Source) / (options.K + rank));

        if (options.Normalize) {
            var maxScore = sets.Sum(set => options.WeightOf(set.Source)) / (options.K + 1);

            foreach (var entry in entries.Values)
                entry.Fused /= maxScore;
        }

        return FusionAccumulator.ToOrderedPool(entries);
    }
}

public sealed class ReciprocalRankFusionOptions {
    /// <summary>The rank-damping constant. 60 is the published default; small values make top ranks dominate hard.</summary>
    public double K { get; set; } = 60;

    /// <summary>Per-source weights keyed by source name; a missing entry means 1.0 (equal-weight RRF).</summary>
    public Dictionary<string, double> Weights { get; set; } = [];

    /// <summary>Rescales fused scores against the theoretical maximum — top rank in every leg — mapping the pool onto (0, 1].</summary>
    public bool Normalize { get; set; }

    internal double WeightOf(string source) => Weights.GetValueOrDefault(source, 1.0);
}
