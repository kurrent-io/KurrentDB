// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The retrieval pipeline: a natural-language query in, ranked memories with a full score
/// breakdown out, best first. Implementations are whole pipelines, each owning its control flow.
/// </summary>
public interface IKontextRetriever {
    ValueTask<IReadOnlyList<ScoredMemory>> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default);
}
