// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>Pins the recency clock and resolves the candidate-pool size from the overfetch policy.</summary>
public sealed class DefaultQueryPlanner(OverfetchOptions overfetch, TimeProvider time) : IQueryPlanner {
    public ValueTask<PlannedQuery> PlanAsync(RetrievalQuery query, CancellationToken ct = default) =>
        ValueTask.FromResult(new PlannedQuery {
            Text     = query.Text,
            Tags     = query.Tags,
            Limit    = query.Limit,
            PoolSize = overfetch.PoolSizeFor(query.Limit),
            AsOf     = query.AsOf ?? time.GetUtcNow(),
        });
}
