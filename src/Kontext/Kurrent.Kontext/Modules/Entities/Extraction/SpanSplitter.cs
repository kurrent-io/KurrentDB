// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.RegularExpressions;

namespace Kurrent.Kontext.Entities.Extraction;

/// <summary>
/// Breaks a coordinated span into the entities it names ("counseling and support groups" → both),
/// since flat NER returns one span per range. If any part fails <see cref="SpanFilter"/> the whole
/// split reverts, keeping genuinely coordinated names like "Marks and Spencer" intact.
/// </summary>
public static partial class SpanSplitter {
    /// <summary>The parts of a coordinated span, or the span itself when it is not one.</summary>
    public static IReadOnlyList<ExtractedEntity> Split(ExtractedEntity entity) {
        var parts = Coordinator().Split(entity.Text);

        if (parts.Length < 2)
            return [entity];

        var split = new List<ExtractedEntity>(parts.Length);

        foreach (var part in parts) {
            var text = part.Trim();

            // One unusable part means the span was not a clean coordination of names.
            if (!SpanFilter.Accepts(text))
                return [entity];

            split.Add(entity with { Text = text });
        }

        return split;
    }

    // Word-boundary "and"/"or", and the comma that lists them. Ampersand included: "Luna & Oliver".
    [GeneratedRegex(@"\s*,\s*|\s+and\s+|\s+or\s+|\s*&\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Coordinator();
}
