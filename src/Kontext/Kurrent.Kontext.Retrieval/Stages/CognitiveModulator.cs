// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Scores each memory on the three ranked dimensions:
/// <para>score = α_recency·recency_norm + α_importance·importance_norm + α_relevance·relevance_norm</para>
/// <para>- recency_raw = e^(−(as_of − last_accessed_at)/tau). Retrieval refreshes last_accessed_at (reconsolidation), so what gets used stays fresh.</para>
/// <para>- importance_raw = the agent's salience level mapped to [0,1].</para>
/// <para>- relevance_raw = the pool's running score (fused, or reranked when a relevance model ran first).</para>
/// <para>- each *_norm is min-max normalized ACROSS THE POOL, so the alphas weigh dimensions, not units.</para>
/// <para>There is no trust multiplier. Trust is enforced when a memory is WRITTEN — store the claim you
/// checked — rather than reconstructed at read time from type and citations. Citations in particular
/// never lift a score: the most rigorous memories carry none, because a check you ran yourself is not
/// a citable source, so rewarding citations would rank a copied blog post above a test you ran.</para>
/// </summary>
public sealed class CognitiveModulator(CognitiveModulationOptions options) : IRetrievalStage {
    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static CognitiveModulator Create(CognitiveModulationOptions options) =>
        new(options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static CognitiveModulator Create(Action<CognitiveModulationOptions>? configure = null) {
        var options = new CognitiveModulationOptions();
        configure?.Invoke(options);
        return Create(options);
    }

    public ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default) {
        if (pool.Count == 0)
            return ValueTask.FromResult(pool);

        var raws = pool.Select(scored => new {
                Scored     = scored,
                Recency    = RecencyOf(scored.Memory, query.AsOf),
                Importance = options.SalienceOf(scored.Memory.Importance),
                Relevance  = scored.Score,
            })
            .ToList();

        var recencyMin    = raws.Min(raw => raw.Recency);
        var recencyMax    = raws.Max(raw => raw.Recency);
        var importanceMin = raws.Min(raw => raw.Importance);
        var importanceMax = raws.Max(raw => raw.Importance);
        var relevanceMin  = raws.Min(raw => raw.Relevance);
        var relevanceMax  = raws.Max(raw => raw.Relevance);

        IReadOnlyList<ScoredMemory> modulated = raws.Select(raw => {
                var recencyNorm    = ScoreNormalization.MinMax(raw.Recency, recencyMin, recencyMax);
                var importanceNorm = ScoreNormalization.MinMax(raw.Importance, importanceMin, importanceMax);
                var relevanceNorm  = ScoreNormalization.MinMax(raw.Relevance, relevanceMin, relevanceMax);

                var baseScore = options.AlphaRecency * recencyNorm
                              + options.AlphaImportance * importanceNorm
                              + options.AlphaRelevance * relevanceNorm;

                return raw.Scored with {
                    Score = baseScore,
                    Breakdown = raw.Scored.Breakdown with {
                        RecencyRaw     = raw.Recency,
                        RecencyNorm    = recencyNorm,
                        ImportanceRaw  = raw.Importance,
                        ImportanceNorm = importanceNorm,
                        RelevanceRaw   = raw.Relevance,
                        RelevanceNorm  = relevanceNorm,
                        BaseScore      = baseScore,
                    },
                };
            })
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Memory.MemoryId, StringComparer.Ordinal)
            .ToList();

        return ValueTask.FromResult(modulated);
    }

    double RecencyOf(MemoryContracts.StoredMemory memory, DateTimeOffset asOf) =>
        ScoreNormalization.ExponentialDecay(asOf - memory.LastAccessedAt.ToDateTimeOffset(), options.RecencyTau);
}

/// <summary>
/// The scoring knobs — weights, tau, and the level→number maps, all tunable so the ranking stays
/// reproducible from the log alone.
/// </summary>
public sealed class CognitiveModulationOptions {
    /// <summary>
    /// Weight of the recency (temporal-decay) dimension in the base score. Kept small: on the
    /// LoCoMo ranking corpus every point above ~0.05 traded measurable relevance for freshness
    /// (nDCG@10 0.317 at 0.05 vs 0.279 at 0.2).
    /// </summary>
    public double AlphaRecency { get; set; } = 0.05;

    /// <summary>Weight of the importance (salience) dimension in the base score.</summary>
    public double AlphaImportance { get; set; } = 0.2;

    /// <summary>Weight of the relevance (query-match) dimension in the base score.</summary>
    public double AlphaRelevance { get; set; } = 0.75;

    /// <summary>
    /// The decay constant: recency_raw = e^(−age/tau). One tau of shelf life leaves ~37% recency.
    /// 30 days, not 7: over a months-long memory span a 7-day tau zeroes everything but the newest
    /// session, and min-max renormalization then hands that session the whole recency dimension.
    /// </summary>
    public TimeSpan RecencyTau { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Salience [0,1] per importance level — the agent's coarse enum resolved to the number the ranking uses.</summary>
    public Dictionary<MemoryContracts.MemoryImportance, double> ImportanceWeights { get; set; } = new() {
        [MemoryContracts.MemoryImportance.Unspecified] = 0.50,
        [MemoryContracts.MemoryImportance.Low]         = 0.25,
        [MemoryContracts.MemoryImportance.Normal]      = 0.50,
        [MemoryContracts.MemoryImportance.High]        = 0.75,
        [MemoryContracts.MemoryImportance.Critical]    = 1.00,
    };

    // TryGetValue first so a present key never touches the fallback lookup (GetValueOrDefault's
    // second argument is evaluated eagerly, so indexing the fallback key unconditionally would
    // throw even for a present key); the fallback lookup itself degrades to a literal neutral
    // instead of indexing, so a partial dictionary that omits Normal/Unspecified can't throw either.
    internal double SalienceOf(MemoryContracts.MemoryImportance importance) =>
        ImportanceWeights.TryGetValue(importance, out var weight)
            ? weight
            : ImportanceWeights.GetValueOrDefault(MemoryContracts.MemoryImportance.Normal, 0.5);
}
