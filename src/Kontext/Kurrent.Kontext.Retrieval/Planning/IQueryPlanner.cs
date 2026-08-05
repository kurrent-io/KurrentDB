// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Prepares a <see cref="RetrievalQuery"/> for the searches: pins the recency clock and resolves the
/// candidate-pool size from the overfetch policy.
/// </summary>
public interface IQueryPlanner {
    ValueTask<PlannedQuery> PlanAsync(RetrievalQuery query, CancellationToken ct = default);
}
