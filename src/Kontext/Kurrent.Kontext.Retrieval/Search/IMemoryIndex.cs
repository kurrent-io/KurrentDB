// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The query surface retrieval needs from the memories read model: three ranked reads, one per
/// search mode. Retrieval owns this port and the read model implements it, so an <see cref="ISearch"/>
/// can live here without the pipeline taking a dependency on the storage engine.
/// <para>Every mode hides superseded memories, and requires ALL of <c>tags</c> to be
/// present on a row for it to surface.</para>
/// <para>The knobs are Lance-shaped (<c>K</c>, <c>RefineFactor</c>, <c>Nprobs</c>, <c>Prefilter</c>).
/// This is the port for THIS index, not a store-neutral abstraction.</para>
/// </summary>
public interface IMemoryIndex {
    /// <summary>Vector mode: ranks by embedding similarity alone. Hits carry <see cref="MemoryHit.VectorDistance"/>, smaller = closer.</summary>
    IAsyncEnumerable<MemoryHit> SearchAsync(
        float[] queryEmbedding,
        IReadOnlyCollection<MemoryContracts.Tag> tags,
        VectorSearchOptions? options = null,
        CancellationToken ct = default);

    /// <summary>Full-text mode: ranks by BM25 over content alone. Hits carry <see cref="MemoryHit.KeywordScore"/>, larger = better.</summary>
    IAsyncEnumerable<MemoryHit> SearchAsync(
        string query,
        IReadOnlyCollection<MemoryContracts.Tag> tags,
        FullTextSearchOptions? options = null,
        CancellationToken ct = default);

    /// <summary>Hybrid mode: the engine's own alpha blend of both legs. Hits carry <see cref="MemoryHit.HybridScore"/>, larger = better.</summary>
    IAsyncEnumerable<MemoryHit> SearchAsync(
        string query,
        float[] queryEmbedding,
        IReadOnlyCollection<MemoryContracts.Tag> tags,
        HybridSearchOptions? options = null,
        CancellationToken ct = default);
}
