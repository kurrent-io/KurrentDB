// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Nudges the pool's running relevance with bounded boosts:
/// <para>final = base × (1 + α_r·(recency − 0.5)) × (1 + α_i·(importance − 0.5))</para>
/// <para>Each signal lives in [0,1] with 0.5 as neutral, so a signal moves the base by at most ±α/2
/// and an absent/average signal is a no-op. Relevance stays king — recency and importance break ties,
/// never overturn a clearly better match.</para>
/// <para>Base is the pool-normalized running score. A degenerate pool (every score identical) falls back
/// to a rank seed so the boosts modulate a real gradient instead of becoming a pure recency sort.</para>
/// </summary>
public sealed class BoostedModulator(BoostModulationOptions options) : IRetrievalStage {
    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static BoostedModulator Create(BoostModulationOptions options) =>
        new(options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static BoostedModulator Create(Action<BoostModulationOptions>? configure = null) {
        var options = new BoostModulationOptions();
        configure?.Invoke(options);
        return Create(options);
    }

    public ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default) {
        if (pool.Count == 0)
            return ValueTask.FromResult(pool);

        var relevanceMin = pool.Min(scored => scored.Score);
        var relevanceMax = pool.Max(scored => scored.Score);
        var degenerate   = relevanceMax - relevanceMin <= double.Epsilon;

        IReadOnlyList<ScoredMemory> modulated = pool.Select((scored, index) => {
                var baseScore = degenerate
                    ? RankSeed(index, pool.Count)
                    : ScoreNormalization.MinMax(scored.Score, relevanceMin, relevanceMax);

                var recency    = ScoreNormalization.HalfLifeDecay(query.AsOf - scored.Memory.LastAccessedAt.ToDateTimeOffset(), options.RecencyHalfLife);
                var importance = options.SalienceOf(scored.Memory.Importance);

                var score = baseScore
                          * (1 + options.RecencyAlpha * (recency - 0.5))
                          * (1 + options.ImportanceAlpha * (importance - 0.5));

                return scored with {
                    Score = score,
                    Breakdown = scored.Breakdown with {
                        RecencyRaw    = recency,
                        ImportanceRaw = importance,
                        BaseScore     = baseScore,
                    },
                };
            })
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Memory.MemoryId, StringComparer.Ordinal)
            .ToList();

        return ValueTask.FromResult(modulated);
    }

    // 1.0 for the pool's first candidate down to 0.1 for its last — the passthrough seed.
    static double RankSeed(int index, int count) =>
        count == 1 ? 1.0 : 1.0 - 0.9 * index / (count - 1);
}

public sealed class BoostModulationOptions {
    /// <summary>Recency swing: the boost spans ±α/2 around neutral (0.2 → at most ±10%).</summary>
    public double RecencyAlpha { get; set; } = 0.2;

    /// <summary>Importance swing: the boost spans ±α/2 around neutral.</summary>
    public double ImportanceAlpha { get; set; } = 0.2;

    /// <summary>Age at which the recency signal reads exactly the neutral 0.5.</summary>
    public TimeSpan RecencyHalfLife { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Salience [0,1] per importance level; NORMAL sits at the neutral 0.5 so it neither boosts nor penalizes.</summary>
    public Dictionary<MemoryContracts.MemoryImportance, double> ImportanceWeights { get; set; } = new() {
        [MemoryContracts.MemoryImportance.Unspecified] = 0.50,
        [MemoryContracts.MemoryImportance.Low]         = 0.25,
        [MemoryContracts.MemoryImportance.Normal]      = 0.50,
        [MemoryContracts.MemoryImportance.High]        = 0.75,
        [MemoryContracts.MemoryImportance.Critical]    = 1.00,
    };

    internal double SalienceOf(MemoryContracts.MemoryImportance importance) =>
        ImportanceWeights.GetValueOrDefault(importance, 0.5);
}
