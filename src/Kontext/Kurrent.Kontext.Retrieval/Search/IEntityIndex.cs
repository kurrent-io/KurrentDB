// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

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
    /// Entity mode: ranks memories by the resolution confidence of the entities they mention,
    /// restricted to entities whose aliases occur in the query text. Hits carry
    /// <see cref="EntityHit.EntityScore"/>, larger = better.
    /// </summary>
    IAsyncEnumerable<EntityHit> SearchAsync(
        string query,
        IReadOnlyCollection<MemoryContracts.Tag> tags,
        EntitySearchOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// One entity-leg result: the stored memory plus the summed resolution confidence of the distinct
/// query-named entities it mentions — larger = better, bounded by the number of matched entities.
/// </summary>
public readonly record struct EntityHit(MemoryContracts.StoredMemory Memory, double EntityScore);

/// <summary>The entity-leg knobs. A mutable settings class by design — config binding does not cope with records.</summary>
public sealed class EntitySearchOptions {
    /// <summary>Rows returned after filtering and ordering.</summary>
    public int Limit { get; set; } = 10;
}
