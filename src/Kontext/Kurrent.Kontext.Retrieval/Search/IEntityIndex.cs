// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The query surface the entity leg needs from the read model: memories linked to entities whose
/// aliases appear in the query text. Retrieval owns this port and the read model implements it,
/// same as <see cref="IMemoryIndex"/>.
/// <para>Hides retracted and superseded memories and requires ALL of <c>tags</c> on a row, the
/// same view of the world as every other leg.</para>
/// </summary>
public interface IEntityIndex {
    /// <summary>
    /// Entity mode: ranks memories by the rarity-weighted count of query-named entities they
    /// mention, restricted to entities whose aliases occur in the query text. Hits carry
    /// <see cref="EntityHit.EntityScore"/>, larger = better.
    /// </summary>
    IAsyncEnumerable<EntityHit> SearchAsync(
        string query,
        IReadOnlyCollection<MemoryContracts.Tag> tags,
        EntitySearchOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// One entity-leg result: the stored memory plus the summed rarity weight (idf) of the distinct
/// query-named entities it mentions — larger = better. Memories matching the same entity set
/// score identically by design: the leg has no opinion between them.
/// </summary>
public readonly record struct EntityHit(MemoryContracts.StoredMemory Memory, double EntityScore);

/// <summary>The entity-leg knobs. A mutable settings class by design — config binding does not cope with records.</summary>
public sealed class EntitySearchOptions {
    /// <summary>Rows returned after filtering and ordering.</summary>
    public int Limit { get; set; } = 10;

    /// <summary>
    /// Hard cap on the leg's candidates, below whatever the planner asks for. Every candidate the
    /// leg emits joins the pool the later stages re-rank, so a permissive leg does not just cast
    /// weak votes — it hands the pool distractors that outscore the answer on raw word overlap.
    /// </summary>
    public int MaxCandidates { get; set; } = int.MaxValue;

    /// <summary>
    /// Whether entities above the document-frequency gate still count toward ORDERING the
    /// admitted candidates. In conversational memories a common name inverts: "when did Melanie
    /// go to the park" is answered by Melanie's own turn — the one turn that never says
    /// "Melanie" — while the other speaker's turns naming her get the conjunction boost.
    /// False scores every query-named entity; true scores only the gate-passing rare ones,
    /// leaving same-entity candidates tied for the text legs to order.
    /// </summary>
    public bool ScoreRareEntitiesOnly { get; set; }

    /// <summary>
    /// A memory competes in the leg only by mentioning an entity rarer than this fraction of
    /// active memories. An entity that appears everywhere (a conversation's own speakers, a
    /// project's name) says nothing about which memories answer the question — letting it admit
    /// candidates floods the pool and rank fusion dilutes the other legs. Common entities still
    /// contribute to ordering, so a conjunction with a rare one outranks the rare one alone.
    /// 1 disables the gate.
    /// <para>Default is the LoCoMo conv-26 measured optimum (see the benchmark's
    /// <c>--entities-ab</c> mode); the two speakers sit at 14% and 31% of memories while genuinely
    /// discriminating entities stay under 2%.</para>
    /// </summary>
    public double MaxDocumentFrequencyRatio { get; set; } = 0.05;

    /// <summary>
    /// Entities mentioned by at most this many active memories always pass the gate, whatever the
    /// ratio says. In a young store the ratio ceiling collapses to nothing — 5% of 40 memories is
    /// two — and without a floor the leg would go silent exactly when every memory matters.
    /// </summary>
    public int MinDocumentFrequency { get; set; } = 3;
}
