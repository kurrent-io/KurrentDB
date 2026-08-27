// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;

namespace Kurrent.Kontext.Entities.Extraction;

/// <summary>
/// The validity gate over merged spans: rejects stopwords, too-short text, purely numeric and
/// punctuation-only forms. Every check reads the normalized surface form, so a multiword name is
/// never held against the single-token stopword set.
/// </summary>
public static partial class SpanFilter {
    const int MinSpanLength = 2;

    /// <summary>Whether the span text can name an entity at all.</summary>
    public static bool Accepts(string text) {
        var normalized = EntityId.Normalize(text);

        return normalized.Length >= MinSpanLength
            && !Stopwords.Contains(normalized)
            && !PurelyNumeric().IsMatch(normalized)
            && !PunctuationOnly().IsMatch(normalized);
    }

    [GeneratedRegex(@"^[\d\s.,%-]+$")]
    private static partial Regex PurelyNumeric();

    [GeneratedRegex(@"^[\s\W]+$")]
    private static partial Regex PunctuationOnly();
}
