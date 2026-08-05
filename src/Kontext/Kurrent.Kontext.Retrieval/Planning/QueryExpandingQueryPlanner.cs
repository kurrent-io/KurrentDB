// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Expands the query text through an <see cref="IQueryExpander"/> before handing it to an inner
/// planner. A decorator, so it composes over any planner without touching the later stages —
/// and expansion changes what BOTH legs search: the keyword leg matches the expanded terms and
/// the vector leg embeds them.
/// </summary>
public sealed class QueryExpandingQueryPlanner(IQueryExpander expander, IQueryPlanner inner) : IQueryPlanner {
    public async ValueTask<PlannedQuery> PlanAsync(RetrievalQuery query, CancellationToken ct = default) {
        var expanded = await expander.ExpandAsync(query.Text, ct).ConfigureAwait(false);

        // Rewrite only the text; every other field (tags, limit, pinned clock) is the caller's intent.
        return await inner.PlanAsync(query with { Text = expanded }, ct).ConfigureAwait(false);
    }
}
