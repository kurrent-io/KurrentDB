// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Rereads the fused pool: a fresh Okapi BM25 index is built over just the candidates and its
/// ranking is rank-fused with the incoming order.
/// <para>Word rarity is computed inside the pool, not the corpus — a query word carried by 3 of 60
/// on-topic candidates usually marks the answer — so this is a genuinely different signal from a
/// corpus-wide keyword leg, for the price of one in-memory pass.</para>
/// </summary>
public sealed partial class Bm25Reranker(Bm25RerankerOptions options) : IRetrievalStage {
    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static Bm25Reranker Create(Bm25RerankerOptions options) =>
        new(options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static Bm25Reranker Create(Action<Bm25RerankerOptions>? configure = null) {
        var opts = new Bm25RerankerOptions();
        configure?.Invoke(opts);
        return Create(opts);
    }

    public ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default) {
        if (pool.Count <= 1)
            return ValueTask.FromResult(pool);

        var docs   = pool.Select(scored => Tokenize(scored.Memory.Content)).ToList();
        var scores = Score(Tokenize(query.Text), docs);

        var byBm25 = Enumerable.Range(0, pool.Count)
            .OrderByDescending(index => scores[index])
            .Select((index, rank) => (index, rank))
            .ToDictionary(entry => entry.index, entry => entry.rank);

        IReadOnlyList<ScoredMemory> reranked = pool
            .Select((scored, index) => scored with {
                Score     = options.IdentityWeight / (options.K + index + 1) + options.Bm25Weight / (options.K + byBm25[index] + 1),
                Breakdown = scored.Breakdown with { Reranked = scores[index] },
            })
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Memory.MemoryId, StringComparer.Ordinal)
            .ToList();

        return ValueTask.FromResult(reranked);
    }

    double[] Score(List<string> queryTokens, List<List<string>> docs) {
        var avgdl = docs.Average(doc => (double)doc.Count);
        var df    = new Dictionary<string, int>();

        foreach (var token in docs.SelectMany(doc => doc.Distinct()))
            df[token] = df.GetValueOrDefault(token) + 1;

        var scores = new double[docs.Count];

        foreach (var token in queryTokens) {
            if (!df.TryGetValue(token, out var n))
                continue;

            var idf = Math.Log(1 + (docs.Count - n + 0.5) / (n + 0.5));

            for (var index = 0; index < docs.Count; index++) {
                var tf = docs[index].Count(t => t == token);

                if (tf > 0)
                    scores[index] += idf * tf * (options.K1 + 1) / (tf + options.K1 * (1 - options.B + options.B * docs[index].Count / avgdl));
            }
        }

        return scores;
    }

    static List<string> Tokenize(string text) =>
        TokenPattern.Matches(text.ToLowerInvariant()).Select(match => match.Value).ToList();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex TokenPattern { get; }
}

/// <summary>The knobs — the defaults are the constants the LoCoMo ranking corpus measured best.</summary>
public sealed class Bm25RerankerOptions {
    /// <summary>Rank damping in the merge — small k lets the reread overrule the incoming order.</summary>
    public double K { get; set; } = 10;

    /// <summary>BM25 term-frequency saturation.</summary>
    public double K1 { get; set; } = 1.5;

    /// <summary>How much long texts get penalized — 0 turns the penalty off (long turns hold the answers).</summary>
    public double B { get; set; }

    /// <summary>Weight of the incoming order in the merge — keeps meaning-only hits with zero word overlap alive.</summary>
    public double IdentityWeight { get; set; } = 1;

    /// <summary>Weight of the pool-local BM25 order in the merge.</summary>
    public double Bm25Weight { get; set; } = 2;
}
