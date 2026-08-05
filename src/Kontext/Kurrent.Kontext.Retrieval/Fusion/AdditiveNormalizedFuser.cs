// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Fuses by score magnitude: fused = (vector relevance + squashed BM25) / active signals.
/// <para>The vector leg is already [0,1]. The keyword leg's unbounded BM25 goes through a logistic sigmoid whose midpoint adapts to query length.</para>
/// <para>Dividing by the signals actually present keeps a keyword-less corpus comparable to a hybrid one.</para>
/// <para>Preserves how much better #1 is than #2 — rank fusion throws that away — but is calibration-sensitive where rank fusion is not.</para>
/// </summary>
public sealed class AdditiveNormalizedFuser(AdditiveFusionOptions options) : ICandidateFuser {
    /// <summary>Creates the fuser from pre-built options — the config-binding door.</summary>
    public static AdditiveNormalizedFuser Create(AdditiveFusionOptions options) =>
        new(options);

    /// <summary>Creates the fuser over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static AdditiveNormalizedFuser Create(Action<AdditiveFusionOptions>? configure = null) {
        var options = new AdditiveFusionOptions();
        configure?.Invoke(options);
        return Create(options);
    }

    public IReadOnlyList<ScoredMemory> Fuse(IReadOnlyList<CandidateSet> sets, PlannedQuery query) {
        var (midpoint, steepness) = options.SigmoidFor(CountTerms(query.Text));

        var anyVector  = false;
        var anyKeyword = false;

        var entries = FusionAccumulator.Collect(sets, (entry, set, candidate, _) => {
            switch (set.Source) {
                case RetrievalSources.Vector:
                    anyVector    =  true;
                    entry.Fused += candidate.Score;
                    break;

                case RetrievalSources.Keyword:
                    anyKeyword   =  true;
                    entry.Fused += ScoreNormalization.Sigmoid(candidate.Score, midpoint, steepness);
                    break;

                default:
                    throw new NotSupportedException(
                        $"Additive fusion only understands the '{RetrievalSources.Vector}' and '{RetrievalSources.Keyword}' legs; source '{set.Source}' has no calibration.");
            }
        });

        // Only signals that actually ran divide the sum — a vector-only pool still tops out at 1.
        var activeSignals = (anyVector ? 1.0 : 0) + (anyKeyword ? 1.0 : 0);

        if (activeSignals > 0)
            foreach (var entry in entries.Values)
                entry.Fused = Math.Min(entry.Fused / activeSignals, 1.0);

        return FusionAccumulator.ToOrderedPool(entries);
    }

    static int CountTerms(string query) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}

public sealed class AdditiveFusionOptions {
    /// <summary>Pin the sigmoid midpoint, disabling the query-length adaptation. Null = adapt (the default).</summary>
    public double? Midpoint { get; set; }

    /// <summary>Pin the sigmoid steepness, disabling the query-length adaptation. Null = adapt (the default).</summary>
    public double? Steepness { get; set; }

    /// <summary>
    /// The adaptive (midpoint, steepness) ladder by query term count. The default rungs were tuned
    /// against Postgres ts_rank_cd magnitudes — RE-TUNE against Lance BM25 before trusting absolute
    /// fused scores.
    /// </summary>
    public List<SigmoidRung> Rungs { get; set; } = [
        new(MaxTerms: 3, Midpoint: 5.0, Steepness: 0.7),
        new(MaxTerms: 6, Midpoint: 7.0, Steepness: 0.6),
        new(MaxTerms: 9, Midpoint: 9.0, Steepness: 0.5),
        new(MaxTerms: 15, Midpoint: 10.0, Steepness: 0.5),
        new(MaxTerms: int.MaxValue, Midpoint: 12.0, Steepness: 0.5),
    ];

    public readonly record struct SigmoidRung(int MaxTerms, double Midpoint, double Steepness);

    internal (double Midpoint, double Steepness) SigmoidFor(int termCount) {
        if (Midpoint is { } midpoint && Steepness is { } steepness)
            return (midpoint, steepness);

        if (Rungs.Count == 0)
            throw new InvalidOperationException(
                "AdditiveFusionOptions.Rungs is empty. Provide at least one rung, or pin both Midpoint and Steepness to skip the ladder entirely.");

        var rung = Rungs.FirstOrDefault(rung => termCount <= rung.MaxTerms, Rungs[^1]);
        return (Midpoint ?? rung.Midpoint, Steepness ?? rung.Steepness);
    }
}
