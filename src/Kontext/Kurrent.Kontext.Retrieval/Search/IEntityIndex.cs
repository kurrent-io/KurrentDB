// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The query surface retrieval needs from the entities read model: recognize the entities a
/// question names, then walk exactly one hop out from them. Retrieval owns this port and the read
/// model implements it, so the entity stage can live here without the pipeline taking a dependency
/// on the storage engine.
/// <para>Cheap by contract. Every read is an equality or containment lookup on surface forms and
/// ids the write side already stored — no model call, no embedding, no fuzzy scoring at question
/// time. An implementation that needs any of those belongs behind a different port.</para>
/// <para>The walk order is fixed: <see cref="MatchAsync"/> first, and its result feeds the ids the
/// other two reads take. An implementation may assume that order (a read model that is not
/// projected yet reports no matches and is never walked).</para>
/// </summary>
public interface IEntityIndex {
    /// <summary>
    /// The entities <paramref name="question"/> names outright — stored normalized names and
    /// aliases matched against the question's own surface forms, under the SAME normalization rule
    /// the write side filed them by. Empty when the question names nothing known, which is the
    /// caller's signal to stop.
    /// <para>Type-blind: a question does not say whether a name is the person or the street, so
    /// every entity carrying the surface comes back and the ranking settles it. A duplicate entity
    /// id may appear at most once.</para>
    /// </summary>
    ValueTask<IReadOnlyList<EntityMatch>> MatchAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// The UNRESOLVED doubt notes touching any of <paramref name="entityIds"/>, in either
    /// direction. Only notes still awaiting a verdict: a retired note is a settled question and
    /// carries no doubt left to price in.
    /// </summary>
    ValueTask<IReadOnlyList<EntityNote>> ListNotesAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default);

    /// <summary>
    /// Which memories mentioned each of <paramref name="entityIds"/> — the provenance walk the
    /// nudge scores over. Provenance is append-only and positional, so the same pair can repeat;
    /// the caller counts a pair once.
    /// </summary>
    ValueTask<IReadOnlyList<EntityMention>> ListMentionsAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default);
}

/// <summary>One entity the question named, with the mention counter the rarity bonus prices it by.</summary>
public readonly record struct EntityMatch(string EntityId, long MentionCount);

/// <summary>
/// One written-down doubt: two entities the write side could not tell apart, and how close they
/// scored. Direction is storage order and carries no meaning — the hop crosses either way.
/// </summary>
public readonly record struct EntityNote(string SourceEntityId, string TargetEntityId, double Confidence);

/// <summary>One entity-to-memory mention: this entity surfaced in this memory.</summary>
public readonly record struct EntityMention(string EntityId, string MemoryId);
