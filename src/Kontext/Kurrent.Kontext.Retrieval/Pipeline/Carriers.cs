// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The state after planning: the caller's query beside its plan. Every later carrier embeds this,
/// so any stage can read the plan (and the cut the caller's limits) without a side channel.
/// </summary>
public readonly record struct Planned(RetrievalQuery Source, PlannedQuery Plan);

/// <summary>The state after the search fan-out: every leg's ranked candidates, awaiting fusion.</summary>
public readonly record struct Searched(Planned Query, IReadOnlyList<CandidateSet> Sets);

/// <summary>
/// The fused, scored pool the stage chain transforms. <typeparamref name="TScale"/> names the
/// scale the running scores live on — a stage that rescales the pool changes the type, and the
/// compiler holds every downstream link to it.
/// </summary>
public readonly record struct Pool<TScale>(Planned Query, IReadOnlyList<ScoredMemory> Memories) where TScale : IScoreScale;
