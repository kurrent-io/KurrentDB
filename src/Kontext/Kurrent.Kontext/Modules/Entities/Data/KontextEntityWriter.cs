// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Surge;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Entities.Data;

/// <summary>
/// Writes one consumed batch of entity events into the catalog tables, one statement per table.
/// Replaying the same batch produces the same rows. Runs on the caller's connection, the
/// projector owns the connection, transaction, and checkpoint. Not thread safe.
/// </summary>
/// <remarks>
/// Nothing here is conditional on what the event says about itself: a mention states the spelling
/// it saw and the entity it named, and the catalog works out for itself whether that spelling is
/// new. Nothing branches on <c>resolved_by</c>, and a "created" mention is just the first one to
/// name its id.
/// </remarks>
public sealed class KontextEntityWriter(
    DuckDBAdvancedConnection connection,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EmbeddingGenerationOptions options
) {
    sealed record PendingAlias(
        string EntityId,
        string EntityType,
        string Alias,
        long   FirstSeenAt
    );

    sealed record PendingMention(
        string MemoryId,
        int    SpanIndex,
        string SpanText,
        string EntityId,
        float  Confidence,
        int    ResolvedBy,
        long   LinkedAt
    );

    /// <summary>Applies one consumed batch of records, ignoring anything that is not an entity event.</summary>
    public async ValueTask<int> ProjectAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct = default) =>
        await ApplyAsync([.. batch.Select(record => record.Value).OfType<EntitiesMentioned>()], ct).ConfigureAwait(false);

    /// <summary>
    /// Applies entity events: collapse them into one pending state per key, then write the
    /// spellings and the mentions. Safe to replay: re-applying the same events writes nothing new,
    /// which is what lets ingestion apply them for read-your-writes before the projector consumes
    /// them again. Returns how many alias rows were inserted.
    /// </summary>
    public async ValueTask<int> ApplyAsync(IReadOnlyCollection<EntitiesMentioned> events, CancellationToken ct = default) {
        var aliases  = new Dictionary<(string EntityId, string Alias), PendingAlias>();
        var mentions = new Dictionary<(string MemoryId, int SpanIndex), PendingMention>();

        foreach (var resolved in events) {
            var resolvedAt = KontextMemoryDataStore.EncodeTimestamp(resolved.ResolvedAt);

            for (var spanIndex = 0; spanIndex < resolved.Mentions.Count; spanIndex++) {
                var mention = resolved.Mentions[spanIndex];

                // An unresolved span is a mention of nothing: recorded so a reconciliation pass can
                // find it, but it names no entity and teaches the catalog no spelling.
                if (mention.HasEntityId)
                    aliases.TryAdd(
                        (mention.EntityId, mention.SpanText),
                        new PendingAlias(mention.EntityId, mention.EntityType, mention.SpanText, resolvedAt));

                mentions[(resolved.MemoryId, spanIndex)] = new PendingMention(
                    resolved.MemoryId, spanIndex, mention.SpanText, mention.EntityId,
                    (float)mention.Confidence, (int)mention.ResolvedBy, resolvedAt);
            }
        }

        if (mentions.Count == 0)
            return 0;

        var written = await ApplyAliases(aliases.Values, ct).ConfigureAwait(false);

        ApplyMentions(mentions.Values);

        return written;
    }

    /// <summary>
    /// Records every spelling the batch saw. Which of them the catalog already holds is the
    /// catalog's answer, not the event's: <see cref="SelectUnknown"/> asks, and only what comes
    /// back gets embedded and inserted. Returns how many rows were written.
    /// </summary>
    /// <remarks>
    /// Asking first is what keeps an exact hit free. Embedding is the expensive part of this
    /// writer, and a mention of a spelling the catalog already indexed must not pay for it.
    /// </remarks>
    async ValueTask<int> ApplyAliases(IReadOnlyCollection<PendingAlias> aliases, CancellationToken ct) {
        if (aliases.Count == 0)
            return 0;

        var unknown = SelectUnknown(aliases);

        if (unknown.Count == 0)
            return 0;

        var sql =
            $"""
             INSERT INTO ldb.main.entities (entity_id, entity_type, alias, first_seen_at, embedding)
             SELECT entity_id, entity_type, alias, first_seen_at,
                    CAST(embedding_raw AS FLOAT[{options.Dimensions}])
             FROM (SELECT
                 unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
                 unnest(CAST($entity_types AS VARCHAR[])) AS entity_type,
                 unnest(CAST($aliases AS VARCHAR[])) AS alias,
                 unnest(CAST($first_seen_ats AS BIGINT[])) AS first_seen_at,
                 unnest(CAST($embeddings AS FLOAT[][])) AS embedding_raw)
             """;

        var aliasTexts = unknown.Select(alias => alias.Alias).ToList();

        var generated = await embeddings
            .GenerateAsync(aliasTexts, cancellationToken: ct)
            .ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        Bind(command, "entity_ids", unknown.Select(alias => alias.EntityId));
        Bind(command, "entity_types", unknown.Select(alias => alias.EntityType));
        Bind(command, "aliases", aliasTexts);
        Bind(command, "first_seen_ats", unknown.Select(alias => alias.FirstSeenAt));
        Bind(command, "embeddings", generated.Select(embedding => embedding.Vector.ToArray()));

        command.ExecuteNonQuery();

        return unknown.Count;
    }

    // Every statement here binds one array per column, so the column name appears once and the
    // rows are read straight off the pending records.
    static void Bind<T>(DuckDBCommand command, string name, IEnumerable<T> values) =>
        command.Parameters.Add(new DuckDBParameter(name, values.ToList()));

    // The spellings the catalog does not hold for their entity, compared case-insensitively: "Mel"
    // and "MEL" are one spelling written two ways, and resolution matches them as one.
    List<PendingAlias> SelectUnknown(IReadOnlyCollection<PendingAlias> aliases) {
        const string sql =
            """
            SELECT s.entity_id, s.alias
            FROM (SELECT
                unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
                unnest(CAST($aliases AS VARCHAR[])) AS alias) AS s
            ANTI JOIN ldb.main.entities AS a
              ON a.entity_id = s.entity_id AND lower(a.alias) = lower(s.alias)
            """;

        var byKey = aliases.ToDictionary(alias => (alias.EntityId, alias.Alias));

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        Bind(command, "entity_ids", aliases.Select(alias => alias.EntityId));
        Bind(command, "aliases", aliases.Select(alias => alias.Alias));

        var unknown = new List<PendingAlias>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            unknown.Add(byKey[(reader.GetString(0), reader.GetString(1))]);

        return unknown;
    }

    // Never called with an empty batch: ProjectAsync returns before it when there are no mentions.
    void ApplyMentions(IReadOnlyCollection<PendingMention> mentions) {
        const string sql =
            """
            MERGE INTO ldb.main.entity_mentions AS t
            USING (SELECT
                unnest(CAST($memory_ids AS VARCHAR[])) AS memory_id,
                unnest(CAST($span_indexes AS INTEGER[])) AS span_index,
                unnest(CAST($span_texts AS VARCHAR[])) AS span_text,
                unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
                unnest(CAST($confidences AS FLOAT[])) AS confidence,
                unnest(CAST($resolved_bys AS INTEGER[])) AS resolved_by,
                unnest(CAST($linked_ats AS BIGINT[])) AS linked_at) AS s
            ON t.memory_id = s.memory_id AND t.span_index = s.span_index
            WHEN NOT MATCHED THEN INSERT (
                memory_id, span_index, span_text, entity_id,
                confidence, resolved_by, linked_at)
            VALUES (
                s.memory_id, s.span_index, s.span_text, s.entity_id,
                s.confidence, s.resolved_by, s.linked_at)
            WHEN MATCHED THEN UPDATE SET
                span_text   = s.span_text
              , entity_id   = s.entity_id
              , confidence  = s.confidence
              , resolved_by = s.resolved_by
              , linked_at   = s.linked_at
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        Bind(command, "memory_ids", mentions.Select(mention => mention.MemoryId));
        Bind(command, "span_indexes", mentions.Select(mention => mention.SpanIndex));
        Bind(command, "span_texts", mentions.Select(mention => mention.SpanText));
        Bind(command, "entity_ids", mentions.Select(mention => mention.EntityId));
        Bind(command, "confidences", mentions.Select(mention => mention.Confidence));
        Bind(command, "resolved_bys", mentions.Select(mention => mention.ResolvedBy));
        Bind(command, "linked_ats", mentions.Select(mention => mention.LinkedAt));

        command.ExecuteNonQuery();
    }
}
