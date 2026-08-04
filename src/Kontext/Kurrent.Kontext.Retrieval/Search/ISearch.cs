// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// One search leg: given the planned query, produce a ranked candidate list from the read
/// model. Searches run in parallel; a failing search fails the retrieval.
/// </summary>
public interface ISearch {
    /// <summary>Stable name used for rank/score provenance and fusion weights.</summary>
    string Name { get; }

    ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default);
}

/// <summary>
/// One candidate as a search saw it: the memory plus the search-native score reoriented so
/// higher = better (vector distances arrive inverted). The list order IS the search's ranking.
/// </summary>
public sealed record SearchCandidate(Contracts.StoredMemory Memory, double Score);

/// <summary>A search's ranked output, best first; <see cref="Source"/> tags which search produced it.</summary>
public sealed record CandidateSet(string Source, IReadOnlyList<SearchCandidate> Candidates);
