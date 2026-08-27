// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Asks a chat model to rate each passage's relevance to the query on a 0..1 scale — one call per passage, temperature 0.
/// <para>The number is parsed and clamped. An unparseable answer falls back to neutral rather than discarding the passage.</para>
/// <para>Concurrency is bounded so a large candidate set doesn't open one request per passage all at once.</para>
/// </summary>
public sealed class ChatRelevanceModel(ChatRelevanceModelOptions options) : IRelevanceModel {
    static readonly Regex FirstNumber = new(@"[-+]?[0-9]*\.?[0-9]+", RegexOptions.Compiled);

    readonly IChatClient _chat = options.Chat
        ?? throw new InvalidOperationException($"{nameof(ChatRelevanceModelOptions)}.{nameof(ChatRelevanceModelOptions.Chat)} is required.");

    /// <summary>Creates the model from pre-built options — the config-binding door.</summary>
    public static ChatRelevanceModel Create(ChatRelevanceModelOptions options) =>
        new(options);

    /// <summary>Creates the model over default options, tuned via <paramref name="configure"/>.</summary>
    public static ChatRelevanceModel Create(Action<ChatRelevanceModelOptions> configure) {
        var options = new ChatRelevanceModelOptions();
        configure(options);
        return Create(options);
    }

    public async ValueTask<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken ct = default) {
        // Not disposed on purpose: a failing passage returns from WhenAll while siblings are still
        // queued, and disposing here would make their Release() throw ObjectDisposedException as an
        // unobserved task exception. SemaphoreSlim only needs disposal once AvailableWaitHandle is read.
        var gate = new SemaphoreSlim(options.MaxConcurrency);

        var chatOptions = new ChatOptions { Temperature = 0, MaxOutputTokens = options.MaxOutputTokens };

        var scores = await Task
            .WhenAll(passages.Select(passage => ScoreOneAsync(query, passage, chatOptions, gate, ct)))
            .ConfigureAwait(false);

        return scores;
    }

    async Task<double> ScoreOneAsync(string query, string passage, ChatOptions chatOptions, SemaphoreSlim gate, CancellationToken ct) {
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try {
            var prompt   = options.BuildPrompt(query, passage);
            var response = await _chat.GetResponseAsync(prompt, chatOptions, ct).ConfigureAwait(false);

            return Parse(response.Text);
        } finally {
            gate.Release();
        }
    }

    // Neutral 0.5 when the model answered with no number — a missing judgment must not silently
    // read as "irrelevant" and drop the passage below the min-score cut.
    double Parse(string? answer) {
        if (answer is null)
            return options.NeutralScore;

        var match = FirstNumber.Match(answer);

        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 1)
            : options.NeutralScore;
    }
}

public sealed class ChatRelevanceModelOptions {
    /// <summary>The chat client the model asks. Required.</summary>
    public IChatClient? Chat { get; set; }

    /// <summary>How many passages are scored at once — the ceiling on concurrent chat requests.</summary>
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>Cap on the model's reply length; the answer is a single number, so this stays tiny.</summary>
    public int MaxOutputTokens { get; set; } = 8;

    /// <summary>The score for a reply that carried no parseable number — neutral, not zero.</summary>
    public double NeutralScore { get; set; } = 0.5;

    /// <summary>
    /// Builds the pointwise-scoring prompt. Default asks for a bare 0..1 number; override to change
    /// the rubric, add few-shot examples, or switch to a yes/no-with-logprobs style.
    /// </summary>
    public Func<string, string, string> BuildPrompt { get; set; } = (query, passage) =>
        $"""
         Rate how relevant the passage is to the query, from 0.0 (irrelevant) to 1.0 (directly answers it).
         Reply with only the number.

         Query: {query}
         Passage: {passage}
         """;
}
