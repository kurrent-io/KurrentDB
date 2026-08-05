// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Greedily rebuilds the list, each step picking the candidate with the best
/// λ·relevance − (1−λ)·max-similarity-to-already-picked (Maximal Marginal Relevance).
/// <para>Buys diversity — near-duplicates stop crowding the top — at the price of pure relevance order.</para>
/// <para>λ = 1 degrades to a plain re-sort. λ ≈ 0.5–0.7 is where diversity actually bites.</para>
/// <para>Reorder-only: changes positions, never scores. Belongs at the end of the chain.</para>
/// <para>Similarity defaults to word-level Jaccard on content — document embeddings are not read back from the store.</para>
/// </summary>
public sealed class MmrReorderer(MmrReordererOptions options) : IRetrievalStage {
    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static MmrReorderer Create(MmrReordererOptions options) =>
        new(options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static MmrReorderer Create(Action<MmrReordererOptions>? configure = null) {
        var options = new MmrReordererOptions();
        configure?.Invoke(options);
        return Create(options);
    }

    public ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default) {
        if (pool.Count <= 1)
            return ValueTask.FromResult(pool);

        var scoreMin = pool.Min(scored => scored.Score);
        var scoreMax = pool.Max(scored => scored.Score);

        var count     = pool.Count;
        var relevance = new double[count];

        // A non-finite incoming score (an upstream fusion that divided by zero) normalizes to NaN,
        // and NaN loses every comparison in the greedy loop below — leaving it with nothing to pick.
        // Treated as the lowest relevance instead, so the pool still comes back whole.
        for (var i = 0; i < count; i++) {
            var normalized = ScoreNormalization.MinMax(pool[i].Score, scoreMin, scoreMax);

            relevance[i] = double.IsFinite(normalized) ? normalized : 0;
        }

        // The greedy loop keeps each candidate's running max-similarity-to-selected and updates it
        // once per pick, so every pair is compared exactly once — O(n²) similarity calls, not the
        // O(n³) a naive rescan of the selected list would cost.
        var maxSimToSelected = new double[count];
        var picked           = new bool[count];
        var selected         = new List<ScoredMemory>(count);

        for (var step = 0; step < count; step++) {
            var bestIndex = -1;
            var bestValue = double.NegativeInfinity;

            for (var i = 0; i < count; i++) {
                if (picked[i])
                    continue;

                var value = options.Lambda * relevance[i] - (1 - options.Lambda) * maxSimToSelected[i];

                if (value > bestValue) {
                    bestValue = value;
                    bestIndex = i;
                }
            }

            picked[bestIndex] = true;

            var pick = pool[bestIndex];
            selected.Add(pick with { Breakdown = pick.Breakdown with { ReorderScore = bestValue } });

            for (var i = 0; i < count; i++) {
                if (picked[i])
                    continue;

                var similarity = options.Similarity(pool[i].Memory.Content, pick.Memory.Content);

                if (similarity > maxSimToSelected[i])
                    maxSimToSelected[i] = similarity;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ScoredMemory>>(selected);
    }
}

public sealed class MmrReordererOptions {
    /// <summary>Relevance-diversity trade-off: 1 = pure relevance (MMR off in effect), 0 = pure diversity.</summary>
    public double Lambda { get; set; } = 0.7;

    /// <summary>Pairwise content similarity in [0,1]. Default: word-level Jaccard.</summary>
    public Func<string, string, double> Similarity { get; set; } = ScoreNormalization.JaccardSimilarity;
}
