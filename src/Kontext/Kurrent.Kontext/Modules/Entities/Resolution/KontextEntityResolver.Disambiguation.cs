// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Contracts.V3.Entities;

namespace Kurrent.Kontext.Entities;

public sealed partial class KontextEntityResolver {
    const double LlmMatchConfidence = 0.95;

    /// <summary>
    /// Disambiguation tier: hands each undecided name with candidates to the model, which picks
    /// one or abstains. A name with no candidates is not asked about — there is nothing to choose
    /// between, and it becomes a new entity.
    /// </summary>
    async ValueTask ClaimDisambiguatedAsync(ResolutionPass pass, CancellationToken ct) {
        if (!pass.Judged)
            return;

        var pending = pass.Unresolved
            .Where(entry => pass.Candidates.ContainsKey(entry.Key))
            .Select(entry => new Disambiguation(entry.Key, entry.Value, pass.Candidates[entry.Key]))
            .ToList();

        if (pending.Count == 0)
            return;

        var chosen = await _disambiguator!.ResolveAsync(pending, ct).ConfigureAwait(false);

        foreach (var (key, entityId) in chosen)
            pass.Claim(key, new ResolvedEntity(entityId, LlmMatchConfidence, ResolutionMethod.Llm));
    }
}
