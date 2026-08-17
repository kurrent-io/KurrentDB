// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Pipelines;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Reranks the head of the pool with an <see cref="IRelevanceModel"/>: the top
/// <see cref="RelevanceModelRerankerOptions.CandidateCap"/> candidates take the model's judgment
/// as their running score (recorded as <see cref="ScoreBreakdown.Reranked"/>), and the tail keeps
/// its order below them — never silently dropped. Runs before any modulating stage on purpose —
/// memory-ness then scales this relevance instead of a score-replacing model overwriting a score
/// memory-ness already shaped.
/// </summary>
public sealed class RelevanceModelReranker<TIn>(IRelevanceModel model, RelevanceModelRerankerOptions options) : IStep<Pool<TIn>, Pool<NativeScale>> where TIn : IScoreScale {
    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static RelevanceModelReranker<TIn> Create(IRelevanceModel model, RelevanceModelRerankerOptions options) =>
        new(model, options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static RelevanceModelReranker<TIn> Create(IRelevanceModel model, Action<RelevanceModelRerankerOptions>? configure = null) {
        var options = new RelevanceModelRerankerOptions();
        configure?.Invoke(options);
        return Create(model, options);
    }

    public async ValueTask<Pool<NativeScale>> Execute(Pool<TIn> input, CancellationToken ct) {
        var (query, pool) = (input.Query.Plan, input.Memories);

        if (pool.Count == 0)
            return new(input.Query, pool);

        var head = pool.Take(options.CandidateCap).ToList();
        var tail = pool.Skip(options.CandidateCap);

        var scores = await model.ScoreAsync(query.Text, head.Select(scored => scored.Memory.Content).ToList(), ct).ConfigureAwait(false);

        if (scores.Count != head.Count)
            throw new InvalidOperationException($"The relevance model returned {scores.Count} scores for {head.Count} passages.");

        var reranked = head
            .Select((scored, index) => scored with {
                Score     = scores[index],
                Breakdown = scored.Breakdown with { Reranked = scores[index] },
            })
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Memory.MemoryId, StringComparer.Ordinal);

        return new(input.Query, reranked.Concat(tail).ToList());
    }
}

public sealed class RelevanceModelRerankerOptions {
    /// <summary>How deep into the pool the model judges — caps the per-pair inference cost.</summary>
    public int CandidateCap { get; set; } = 100;
}
