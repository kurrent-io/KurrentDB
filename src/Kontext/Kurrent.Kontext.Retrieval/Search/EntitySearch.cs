// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The entity leg: surfaces memories that mention the entities the query names, resolved through
/// the entity catalog — the graph hop embeddings and keywords both miss when a memory talks about
/// an entity under a different alias than the query uses.
/// </summary>
public sealed class EntitySearch(IEntityIndex index, Action<EntitySearchOptions>? configure = null) : ISearch {
    public string Name => RetrievalSources.Entity;

    public async ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) {
        var options = new EntitySearchOptions();
        configure?.Invoke(options);
        options.Limit = Math.Min(query.PoolSize, options.MaxCandidates);

        var candidates = new List<SearchCandidate>(query.PoolSize);

        await foreach (var hit in index.SearchAsync(query.Text, query.Tags, options, ct).ConfigureAwait(false))
            candidates.Add(new(hit.Memory, hit.EntityScore));

        return new(Name, candidates);
    }
}
