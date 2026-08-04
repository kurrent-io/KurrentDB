// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// A dependency-free expander: appends known synonyms to each query token. Deterministic,
/// order-preserving, no duplicates.
/// </summary>
public sealed class SynonymQueryExpander(IReadOnlyDictionary<string, IReadOnlyList<string>> synonyms) : IQueryExpander {
    public ValueTask<string> ExpandAsync(string query, CancellationToken ct = default) {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var expanded = new List<string>(terms.Length);
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms) {
            if (seen.Add(term))
                expanded.Add(term);

            if (!synonyms.TryGetValue(term, out var alternates))
                continue;

            foreach (var alternate in alternates)
                if (seen.Add(alternate))
                    expanded.Add(alternate);
        }

        return ValueTask.FromResult(string.Join(' ', expanded));
    }
}
