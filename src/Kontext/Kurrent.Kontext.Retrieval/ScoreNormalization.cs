// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The score arithmetic shared by fusers and scorers — pure functions, no state, trivially testable.
/// </summary>
public static class ScoreNormalization {
    /// <summary>
    /// Turns a Lance vector distance (smaller = closer, squared L2 under the default metric) into
    /// a bounded higher-is-better relevance: 1 / (1 + d). Monotone for any non-negative metric, so
    /// it is safe without assuming normalized embeddings.
    /// </summary>
    public static double RelevanceFromDistance(double distance) =>
        1.0 / (1.0 + Math.Max(0, distance));

    /// <summary>
    /// Logistic squash for an unbounded score (raw BM25): 1 / (1 + e^(-steepness·(value − midpoint))).
    /// The midpoint is the raw score that maps to 0.5.
    /// </summary>
    public static double Sigmoid(double value, double midpoint, double steepness) =>
        1.0 / (1.0 + Math.Exp(-steepness * (value - midpoint)));

    /// <summary>
    /// Min-max normalization against pool bounds. A degenerate pool (max == min) has nothing to
    /// discriminate on, so every value maps to the neutral 0.5 instead of an arbitrary 0 or 1.
    /// </summary>
    public static double MinMax(double value, double min, double max) =>
        max - min <= double.Epsilon ? 0.5 : Math.Clamp((value - min) / (max - min), 0, 1);

    /// <summary>Exponential decay e^(−age/tau): 1 at age zero, ~0.37 at one tau. Future-dated ages clamp to full freshness, never a penalty.</summary>
    public static double ExponentialDecay(TimeSpan age, TimeSpan tau) =>
        age <= TimeSpan.Zero ? 1.0 : Math.Exp(-(age / tau));

    /// <summary>Half-life decay 0.5^(age/halfLife): 1 at age zero, exactly 0.5 at one half-life.</summary>
    public static double HalfLifeDecay(TimeSpan age, TimeSpan halfLife) =>
        age <= TimeSpan.Zero ? 1.0 : Math.Pow(0.5, age / halfLife);

    /// <summary>
    /// Word-shingle Jaccard similarity between two contents — the embedding-free similarity the
    /// diversity reranker runs on (document embeddings are not read back from the store).
    /// </summary>
    public static double JaccardSimilarity(string left, string right) {
        var a = Tokenize(left);
        var b = Tokenize(right);

        if (a.Count == 0 || b.Count == 0)
            return 0;

        // The comparer is explicit: Enumerable.Intersect builds its own default-comparer set and
        // would otherwise discard the OrdinalIgnoreCase one Tokenize gave both sides, making the
        // intersection case-sensitive while each side stayed case-insensitively deduplicated.
        var intersection = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    static HashSet<string> Tokenize(string text) {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (token.Length > 1)
                tokens.Add(token);

        return tokens;
    }

    static readonly char[] Separators = [' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'];
}
