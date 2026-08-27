// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>Shared fusion plumbing: accumulate per-memory provenance across sets, emit a totally ordered pool.</summary>
static class FusionAccumulator {
    internal sealed class Entry {
        public required MemoryContracts.StoredMemory Memory { get; init; }

        public double Fused { get; set; }

        public Dictionary<string, int> Ranks { get; } = [];

        public Dictionary<string, double> Scores { get; } = [];
    }

    /// <summary>
    /// Walks every set once, registering rank/score provenance; <paramref name="accumulate"/> owns
    /// the fused-score math. Equal scores share a competition rank (1, 1, 3): a leg that cannot
    /// tell two candidates apart must not cast distinct votes for them — the list order of a tie
    /// run is storage noise, and rank fusion would amplify it into a real ranking signal.
    /// </summary>
    internal static Dictionary<string, Entry> Collect(IReadOnlyList<CandidateSet> sets, Action<Entry, CandidateSet, SearchCandidate, int> accumulate) {
        var entries = new Dictionary<string, Entry>();

        foreach (var set in sets) {
            var rank      = 0;
            var prevScore = double.NaN;

            for (var index = 0; index < set.Candidates.Count; index++) {
                var candidate = set.Candidates[index];

                if (candidate.Score != prevScore) {
                    rank      = index + 1;
                    prevScore = candidate.Score;
                }

                if (!entries.TryGetValue(candidate.Memory.MemoryId, out var entry))
                    entries[candidate.Memory.MemoryId] = entry = new() { Memory = candidate.Memory };

                entry.Ranks[set.Source]  = rank;
                entry.Scores[set.Source] = candidate.Score;

                accumulate(entry, set, candidate, rank);
            }
        }

        return entries;
    }

    /// <summary>Fused score descending, memory id as the tiebreak — two identical runs return the same order.</summary>
    internal static IReadOnlyList<ScoredMemory> ToOrderedPool(Dictionary<string, Entry> entries) =>
        entries.Values
            .OrderByDescending(entry => entry.Fused)
            .ThenBy(entry => entry.Memory.MemoryId, StringComparer.Ordinal)
            .Select(entry => new ScoredMemory {
                Memory = entry.Memory,
                Score  = entry.Fused,
                Breakdown = new() {
                    Fused        = entry.Fused,
                    SourceRanks  = entry.Ranks,
                    SourceScores = entry.Scores,
                },
            })
            .ToList();
}
