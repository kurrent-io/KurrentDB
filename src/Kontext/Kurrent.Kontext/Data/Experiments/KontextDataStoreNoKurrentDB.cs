// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

namespace Kurrent.Kontext.Data;

/// <summary>
/// The data access layer for everything Kontext persists — today one "memories" vector collection
/// reached through the portable <see cref="VectorStore"/> abstraction; entities, directives, and the
/// other upcoming stores land here too. This is the ONLY class that knows how memories are
/// physically stored and found: consumers speak contract types and memory-shaped verbs, never
/// records, filters, or connector types. When the portable surface can't express an operation
/// (partial-update stamps, hybrid-search tuning, maintenance), this is the one place allowed to
/// reach past it straight into the backing engine (e.g. DuckDB) without anything above noticing.
/// </summary>
public sealed class KontextDataStoreNoKurrentDB : IDisposable, IAsyncDisposable {
    const string MemoriesCollection = "memories";

    public KontextDataStoreNoKurrentDB(VectorStore vectorStore, TouchBufferOptions? touchBuffering = null) {
        _memories = vectorStore.GetCollection<string, MemoryRecord>(MemoriesCollection);

        // The touch buffer is opt-in; without it, access stamps stay on the request path.
        if (touchBuffering is { Enabled: true } buffering)
            _touchBuffer = new TouchBuffer(_memories, buffering);
    }

    readonly VectorStoreCollection<string, MemoryRecord> _memories;
    readonly TouchBuffer?                                _touchBuffer;

    volatile bool _schemaReady;

    /// <summary>
    /// Creates whatever storage objects are missing — today the memories collection. Every operation
    /// calls this first, so consumers never think about schema; hosts may call it eagerly to warm up.
    /// </summary>
    // EnsureCollectionExistsAsync is idempotent, so the benign race between concurrent first calls
    // is fine — the flag only spares the round-trip on every subsequent operation, and a failure is
    // never cached.
    public async ValueTask EnsureSchemaAsync(CancellationToken ct = default) {
        if (_schemaReady)
            return;

        await _memories
            .EnsureCollectionExistsAsync(ct)
            .ConfigureAwait(false);
        
        _schemaReady = true;
        
    }

    /// <summary>The stored memory with the given id, or null. Never hides: retracted and superseded memories come back too.</summary>
    public async ValueTask<Contracts.StoredMemory?> GetAsync(string memoryId, CancellationToken ct = default) {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var record = await _memories.GetAsync(memoryId, cancellationToken: ct).ConfigureAwait(false);
        return record is null ? null : MemoryRecordMapper.ToStoredMemory(record);
    }

    /// <summary>Streams the stored memories for the given ids; ids that don't exist are simply absent.</summary>
    public async IAsyncEnumerable<Contracts.StoredMemory> GetAsync(IEnumerable<string> memoryIds, [EnumeratorCancellation] CancellationToken ct = default) {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        await foreach (var record in _memories.GetAsync(memoryIds, cancellationToken: ct).ConfigureAwait(false))
            yield return MemoryRecordMapper.ToStoredMemory(record);
    }

    // this needs to disapear because kurrentdb is the source of truth for memories, and it needs to be able to update them without going through the store.
    /// <summary>
    /// Saves the batch in ONE storage commit — new memories and lifecycle-stamped ones alike — so the
    /// backing connector also generates whatever embeddings it needs once, not per memory.
    /// </summary>
    public async ValueTask SaveAsync(IReadOnlyCollection<Contracts.StoredMemory> memories, CancellationToken ct = default) {
        if (memories.Count == 0)
            return;

        await EnsureSchemaAsync(ct).ConfigureAwait(false);
        await _memories.UpsertAsync(memories.Select(MemoryRecordMapper.ToRecord), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Relevance search over what is currently true — never retracted, never superseded — carrying
    /// every requested tag. Scores are the connector's similarity (larger = better).
    /// </summary>
    public async IAsyncEnumerable<MemorySearchHit> SearchAsync(
        string query, int limit, IReadOnlyCollection<Contracts.Tag> tags,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var options = new VectorSearchOptions<MemoryRecord> {
            Filter = MemoryRecordFilters.Recall(tags.Select(MemoryRecordMapper.EncodeTag)),
        };

        await foreach (var hit in _memories.SearchAsync(query, limit, options, ct).ConfigureAwait(false))
            yield return new MemorySearchHit(MemoryRecordMapper.ToStoredMemory(hit.Record), hit.Score ?? 0);
    }

    /// <summary>
    /// Lists memories by tags and (any-of) types, sorted and limited. Retracted memories never come
    /// back; superseded ones stay readable history. Plain listing, no relevance — a relational
    /// query, so on DuckDB it runs as ONE SQL statement; the portable path exists for every other
    /// <see cref="VectorStore"/> (e.g. the in-memory test double).
    /// </summary>
    public async IAsyncEnumerable<Contracts.StoredMemory> ListAsync(
        IReadOnlyCollection<Contracts.Tag> tags,
        IReadOnlyCollection<Contracts.MemoryType> types,
        Contracts.RecollectSort sort,
        Contracts.SortDirection direction,
        int limit,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var records = _memories is DuckDBCollection<string, MemoryRecord> duck
            ? await ListBySqlAsync(duck, tags, types, sort, direction, limit, ct).ConfigureAwait(false)
            : await ListByPortableSurfaceAsync(tags, types, sort, direction, limit, ct).ConfigureAwait(false);

        foreach (var record in records)
            yield return MemoryRecordMapper.ToStoredMemory(record);
    }

    // The DuckDB path: the engine does the any-of (IN), the ALL-tags containment (array_has_all),
    // the ordering and the limit in one statement. Column names are the record's storage names
    // (DuckLance lowercases every property name).
    static Task<IReadOnlyList<MemoryRecord>> ListBySqlAsync(
        DuckDBCollection<string, MemoryRecord> memories,
        IReadOnlyCollection<Contracts.Tag> tags, 
        IReadOnlyCollection<Contracts.MemoryType> types,
        Contracts.RecollectSort sort, 
        Contracts.SortDirection direction,
        int limit, CancellationToken ct
    ) {
        var criteria   = new StringBuilder("WHERE isretracted = false");
        var parameters = new List<object?>();

        if (types.Count > 0) {
            var distinct = types.Distinct().ToList();

            criteria.Append($" AND memorytype IN ({string.Join(", ", Enumerable.Repeat("?", distinct.Count))})");
            parameters.AddRange(distinct.Select(type => (object?)(int)type));
        }

        if (tags.Count > 0) {
            // ALL requested tags must be present — the same semantics the portable filter builds
            // with one containment predicate per tag.
            criteria.Append(" AND array_has_all(tags, CAST(? AS VARCHAR[]))");
            parameters.Add(tags.Select(MemoryRecordMapper.EncodeTag).ToList());
        }

        criteria.Append($" ORDER BY {SortColumn(sort)} {(direction == Contracts.SortDirection.Ascending ? "ASC" : "DESC")} LIMIT {limit}");

        return memories.QueryAsync(criteria.ToString(), parameters, includeVectors: false, ct);

        static string SortColumn(Contracts.RecollectSort sort) => sort switch {
            Contracts.RecollectSort.LastAccessedAt => "lastaccessedat",
            Contracts.RecollectSort.Importance     => "importance",
            _                                      => "retainedat", // default: when the memory was recorded
        };
    }

    // The portable path, for stores with no SQL underneath: the filter surface has no OR, so the
    // any-of type set becomes one query per type — each already sorted and limited server-side —
    // and the merge re-sorts and re-limits client-side.
    async Task<IReadOnlyList<MemoryRecord>> ListByPortableSurfaceAsync(
        IReadOnlyCollection<Contracts.Tag> tags, IReadOnlyCollection<Contracts.MemoryType> types,
        Contracts.RecollectSort sort, Contracts.SortDirection direction, int limit, CancellationToken ct
    ) {
        var encodedTags = tags.Select(MemoryRecordMapper.EncodeTag).ToList();
        var descending  = direction != Contracts.SortDirection.Ascending;

        var options = new FilteredRecordRetrievalOptions<MemoryRecord> {
            OrderBy = order => descending ? order.Descending(SortKey(sort)) : order.Ascending(SortKey(sort)),
        };

        List<int?> typeFilters = types.Count == 0
            ? [null]
            : [.. types.Distinct().Select(type => (int?)(int)type)];

        var merged = new List<MemoryRecord>();

        foreach (var typeFilter in typeFilters) {
            var records = _memories.GetAsync(MemoryRecordFilters.Recollect(encodedTags, typeFilter), limit, options, ct);

            await foreach (var record in records.ConfigureAwait(false))
                merged.Add(record);
        }

        var compareKey = CompareKey(sort);
        var ordered    = descending ? merged.OrderByDescending(compareKey) : merged.OrderBy(compareKey);

        return [.. ordered.Take(limit)];
    }

    /// <summary>Streams the non-retracted memories whose evidence cites the given id — the retract cascade's fan-out.</summary>
    public async IAsyncEnumerable<Contracts.StoredMemory> FindDerivedAsync(string citedMemoryId, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var derived = _memories.GetAsync(MemoryRecordFilters.DerivedFrom(citedMemoryId), limit, cancellationToken: ct);

        await foreach (var record in derived.ConfigureAwait(false))
            yield return MemoryRecordMapper.ToStoredMemory(record);
    }

    /// <summary>
    /// Advances the recency clock on the given memories — the caller decides when a read counts as
    /// access. With the opt-in buffer the stamps coalesce off the request path; unbuffered, they land
    /// before this call returns. Today a stamp re-upserts the whole record (the portable surface has
    /// no partial update, so the connector also re-embeds); a direct engine UPDATE can replace that
    /// here without consumers noticing.
    /// </summary>
    public async ValueTask MarkAccessedAsync(IReadOnlyList<Contracts.StoredMemory> memories, CancellationToken ct = default) {
        if (memories.Count == 0)
            return;

        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var now     = DateTimeOffset.UtcNow;
        var records = new List<MemoryRecord>(memories.Count);

        foreach (var memory in memories)
            records.Add(MemoryRecordMapper.ToRecord(memory));

        if (_touchBuffer is not null) {
            // The buffer stamps LastAccessedAt itself as it coalesces (last touch wins per id).
            _touchBuffer.Add(records, now);
            return;
        }

        foreach (var record in records)
            record.LastAccessedAt = now;

        await _memories.UpsertAsync(records, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Flushes any buffered access stamps. The sync path is best-effort (the remaining flush is fired
    /// without being awaited); prefer async disposal.
    /// </summary>
    public ValueTask DisposeAsync() => _touchBuffer?.DisposeAsync() ?? ValueTask.CompletedTask;

    public void Dispose() => _touchBuffer?.Dispose();

    // Server-side ordering key, in the abstraction's boxed OrderBy shape.
    static Expression<Func<MemoryRecord, object?>> SortKey(Contracts.RecollectSort sort) => sort switch {
        Contracts.RecollectSort.LastAccessedAt => r => r.LastAccessedAt,
        Contracts.RecollectSort.Importance     => r => r.Importance,
        _                                      => r => r.RetainedAt, // default: when the memory was recorded
    };

    // Client-side key for merging the per-type queries — must order exactly like SortKey.
    static Func<MemoryRecord, IComparable> CompareKey(Contracts.RecollectSort sort) => sort switch {
        Contracts.RecollectSort.LastAccessedAt => r => r.LastAccessedAt,
        Contracts.RecollectSort.Importance     => r => r.Importance,
        _                                      => r => r.RetainedAt,
    };
}
