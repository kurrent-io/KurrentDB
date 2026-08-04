// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Merges the sources' ranked lists into one deduplicated, scored pool — the list the stage chain
/// transforms from there. The fused score becomes the pool's running <see cref="ScoredMemory.Score"/>,
/// with per-source rank/score provenance on the breakdown. Pure and synchronous by design: fusion
/// is arithmetic over lists already in memory, and it sees ranks and scores, never memory contents.
/// </summary>
public interface ICandidateFuser {
    IReadOnlyList<ScoredMemory> Fuse(IReadOnlyList<CandidateSet> sets, PlannedQuery query);
}
