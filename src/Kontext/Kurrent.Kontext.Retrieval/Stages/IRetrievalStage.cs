// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// One pass over the scored pool between fusion and the final cut — rerank, modulate, reorder,
/// budget. Stages run in the order the pipeline added them, each receiving the previous stage's
/// output; the empty chain leaves the fused order standing.
/// </summary>
public interface IRetrievalStage {
    ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default);
}
