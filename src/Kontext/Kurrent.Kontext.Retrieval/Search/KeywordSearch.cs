// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The literal leg: ranks memories by BM25 keyword relevance alone — the exact-token matches
/// embeddings blur. Scores are raw BM25: unbounded and corpus-relative.
/// </summary>
public sealed class KeywordSearch(IMemoryIndex index) : ISearch {
    public string Name => RetrievalSources.Keyword;

    public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
        var options = new FullTextSearchOptions { Limit = query.PoolSize, K = query.PoolSize };

        var candidates = new List<SearchCandidate>(query.PoolSize);

        await foreach (var hit in index.SearchAsync(query.Text, query.Tags, options, ct).ConfigureAwait(false)) {
            var score = hit.KeywordScore ?? throw new InvalidOperationException("The keyword leg returned a hit without a BM25 score.");
            candidates.Add(new(hit.Memory, score));
        }

        return new(Name, candidates);
    }
}
