// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Scores each passage by word-shingle Jaccard overlap with the query.
/// <para>NOT a cross-encoder — sees only shared words, never meaning.</para>
/// <para>Deterministic, no model, no network: a stable test double for the reranker plumbing.</para>
/// </summary>
public sealed class LexicalRelevanceModel : IRelevanceModel {
    public ValueTask<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken ct = default) {
        IReadOnlyList<double> scores = passages
            .Select(passage => ScoreNormalization.JaccardSimilarity(query, passage))
            .ToList();

        return ValueTask.FromResult(scores);
    }
}
