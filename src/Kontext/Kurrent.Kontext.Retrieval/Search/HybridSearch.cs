// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Both legs in one engine call: Lance's hybrid search runs the vector and keyword scans itself
/// and blends their min-max-normalized scores by alpha. Scores are <c>_hybrid_score</c> —
/// bounded, higher = better, never comparable across queries.
/// </summary>
public sealed class HybridSearch(
    IMemoryIndex index,
    EmbeddingGenerator embeddingGenerator,
    double alpha = 0.5,
    Action<HybridSearchOptions>? tune = null
) : ISearch {
    public string Name => RetrievalSources.Hybrid;

    public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
        // Hybrid is asked for exactly the pool it should return — no oversampling at this
        // surface. How the engine staffs its internal legs is the engine's business.
        var options = new HybridSearchOptions {
            Limit = query.PoolSize, // we can remove this cause no oversampling...
            K     = query.PoolSize,
            Alpha = alpha,
        };

        // The engine-knob escape hatch (use_index, nprobs, refine_factor) — benchmarks and
        // parity checks reach past the per-query defaults here.
        tune?.Invoke(options);

        var embedding = await embeddingGenerator
            .EmbedQueryAsync(query.Text, ct)
            .ConfigureAwait(false);

        var candidates = new List<SearchCandidate>(query.PoolSize);

        await foreach (var hit in index.SearchAsync(query.Text, embedding, query.Tags, options, ct).ConfigureAwait(false)) {
            var score = hit.HybridScore ?? throw new InvalidOperationException("The hybrid leg returned a hit without a blend score.");
            candidates.Add(new(hit.Memory, score));
        }

        return new(Name, candidates);
    }
}
