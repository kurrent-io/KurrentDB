// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Passes a single search's scores through untouched — for keyword-only, vector-only, or engine-hybrid pipelines.
/// <para>Demands exactly one candidate set so a misassembled pipeline fails loud instead of silently averaging.</para>
/// </summary>
public sealed class IdentityFuser : ICandidateFuser {
    public IReadOnlyList<ScoredMemory> Fuse(IReadOnlyList<CandidateSet> sets, PlannedQuery query) {
        if (sets.Count != 1)
            throw new InvalidOperationException($"IdentityFuser expects exactly one candidate source, got {sets.Count} — multi-search pipelines need a real fuser.");

        var entries = FusionAccumulator.Collect(sets, (entry, _, candidate, _) => entry.Fused = candidate.Score);

        return FusionAccumulator.ToOrderedPool(entries);
    }
}
