// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// A model that reads the query and a memory TOGETHER and judges relevance — a cross-encoder, a
/// hosted rerank API, or an LLM scoring pass. Stronger than any same-space similarity and more
/// expensive: it runs per (query, memory) pair, which is why it reranks only the head of an
/// already-narrowed pool.
/// </summary>
public interface IRelevanceModel {
    /// <summary>Relevance in [0,1] per passage, same order as the input.</summary>
    ValueTask<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken ct = default);
}
