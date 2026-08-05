// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Takes every source's #1, then every source's #2, and so on, deduplicated first-wins.
/// <para>Guarantees each leg's top pick a slot — the antidote to rank fusion burying a memory that is #1 in one leg but absent from the other.</para>
/// <para>Fused scores are strictly decreasing positions, meaningful only as an ordering.</para>
/// </summary>
public sealed class InterleaveFuser : ICandidateFuser {
    public IReadOnlyList<ScoredMemory> Fuse(IReadOnlyList<CandidateSet> sets, PlannedQuery query) {
        var entries = FusionAccumulator.Collect(sets, (_, _, _, _) => { });

        var ordered = new List<FusionAccumulator.Entry>(entries.Count);
        var seen    = new HashSet<string>(entries.Count);
        var deepest = sets.Count == 0 ? 0 : sets.Max(set => set.Candidates.Count);

        for (var depth = 0; depth < deepest; depth++)
        foreach (var set in sets) {
            if (depth >= set.Candidates.Count)
                continue;

            var memoryId = set.Candidates[depth].Memory.MemoryId;

            if (seen.Add(memoryId))
                ordered.Add(entries[memoryId]);
        }

        for (var position = 0; position < ordered.Count; position++)
            ordered[position].Fused = ordered.Count - position;

        return FusionAccumulator.ToOrderedPool(entries);
    }
}
