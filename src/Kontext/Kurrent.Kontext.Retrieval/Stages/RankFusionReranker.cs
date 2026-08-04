// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Refines the pool with a second-stage rank fusion: RRF over the incoming rank and the relevance model's rank.
/// <para>Keeps only the union of the two top-<see cref="RankFusionRerankerOptions.PoolSize"/> pools,
/// normalized against the two-leg maximum. Candidates outside both pools are dropped.</para>
/// </summary>
public sealed class RankFusionReranker(IRelevanceModel model, RankFusionRerankerOptions options) : IRetrievalStage {
    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static RankFusionReranker Create(IRelevanceModel model, RankFusionRerankerOptions options) =>
        new(model, options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static RankFusionReranker Create(IRelevanceModel model, Action<RankFusionRerankerOptions>? configure = null) {
        var options = new RankFusionRerankerOptions();
        configure?.Invoke(options);
        return Create(model, options);
    }

    public async ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default) {
        if (pool.Count == 0)
            return pool;

        var modelScores = await model.ScoreAsync(query.Text, pool.Select(scored => scored.Memory.Content).ToList(), ct).ConfigureAwait(false);

        if (modelScores.Count != pool.Count)
            throw new InvalidOperationException($"The relevance model returned {modelScores.Count} scores for {pool.Count} passages.");

        var fused = new Dictionary<string, double>(options.PoolSize * 2);

        for (var index = 0; index < Math.Min(options.PoolSize, pool.Count); index++)
            fused[pool[index].Memory.MemoryId] = 1.0 / (options.K + index + 1);

        var byModel = pool
            .Select((scored, index) => (scored.Memory.MemoryId, Score: modelScores[index]))
            .OrderByDescending(entry => entry.Score)
            .Take(options.PoolSize)
            .ToList();

        for (var index = 0; index < byModel.Count; index++) {
            var id = byModel[index].MemoryId;
            fused[id] = fused.GetValueOrDefault(id) + 1.0 / (options.K + index + 1);
        }

        var maxScore = 2.0 / (options.K + 1);

        return pool
            .Select((scored, index) => (Scored: scored, ModelScore: modelScores[index]))
            .Where(entry => fused.ContainsKey(entry.Scored.Memory.MemoryId))
            .Select(entry => entry.Scored with {
                Score     = fused[entry.Scored.Memory.MemoryId] / maxScore,
                Breakdown = entry.Scored.Breakdown with { Reranked = entry.ModelScore },
            })
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Memory.MemoryId, StringComparer.Ordinal)
            .ToList();
    }
}

public sealed class RankFusionRerankerOptions {
    /// <summary>The rank-damping constant. 60 is the published default; small values make top ranks dominate hard.</summary>
    public double K { get; set; } = 60;

    /// <summary>How deep each leg's top pool reaches — only the union of the two pools survives.</summary>
    public int PoolSize { get; set; } = 20;
}
