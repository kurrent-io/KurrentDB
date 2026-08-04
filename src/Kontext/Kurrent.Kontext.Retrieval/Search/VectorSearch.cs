// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The meaning leg: ranks memories by embedding similarity to the query alone. Scores are
/// <see cref="ScoreNormalization.RelevanceFromDistance"/> of the Lance distance — bounded,
/// higher = better.
/// </summary>
public sealed class VectorSearch(IMemoryIndex index, EmbeddingGenerator embeddingGenerator) : ISearch {
    public string Name => RetrievalSources.Vector;

    public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
        var options = new VectorSearchOptions { Limit = query.PoolSize, K = query.PoolSize };

        var embedding = await embeddingGenerator.EmbedQueryAsync(query.Text, ct).ConfigureAwait(false);

        var candidates = new List<SearchCandidate>(query.PoolSize);

        await foreach (var hit in index.SearchAsync(embedding, query.Tags, options, ct).ConfigureAwait(false)) {
            var distance = hit.VectorDistance ?? throw new InvalidOperationException("The vector leg returned a hit without a distance.");
            candidates.Add(new(hit.Memory, ScoreNormalization.RelevanceFromDistance(distance)));
        }

        return new(Name, candidates);
    }
}
