// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Entities;

public sealed partial class KontextEntityResolver {
    /// <summary>
    /// Disambiguation tier: hands each undecided name with candidates to the decider, which picks
    /// one or abstains. A name with no candidates is not asked about — there is nothing to choose
    /// between, and it becomes a new entity.
    /// </summary>
    async ValueTask ClaimDisambiguatedAsync(ResolutionPass pass, CancellationToken ct) {
        var pending = pass.Undecided
            .Where(entry => entry.Value.Candidates.Count > 0)
            .Select(entry => new Disambiguation(entry.Key, entry.Value.Text, entry.Value.Candidates))
            .ToList();

        if (pending.Count == 0)
            return;

        var chosen = await _disambiguator.ResolveAsync(pending, ct).ConfigureAwait(false);

        foreach (var (key, resolution) in chosen)
            pass.Claim(key, resolution);
    }
}
