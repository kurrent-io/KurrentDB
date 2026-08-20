// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Surge;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Modules.Entities.Data;

/// <summary>
/// Writes one consumed batch of entity events into the catalog tables, one MERGE per table.
/// Replaying the same batch produces the same rows. Runs on the caller's connection, the
/// projector owns the connection, transaction, and checkpoint. Not thread safe.
/// </summary>
public sealed class KontextEntityWriter(
    DuckDBAdvancedConnection connection,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EmbeddingGenerationOptions options
) {
    sealed class PendingAlias(string entityId, string entityType, string alias, bool isCanonical, long createdAt, long logPosition) {
        public string EntityId    { get; } = entityId;
        public string EntityType  { get; } = entityType;
        public string Alias       { get; } = alias;
        public bool   IsCanonical { get; } = isCanonical;
        public long   CreatedAt   { get; } = createdAt;
        public long   LogPosition { get; } = logPosition;

        public float[] Embedding { get; private set; } = [];

        public void Embed(float[] embedding) => Embedding = embedding;
    }

    sealed record PendingMention(
        string MemoryId,
        int    SpanIndex,
        string SpanText,
        string EntityId,
        float  Confidence,
        int    ResolvedBy,
        long   LinkedAt,
        long   LogPosition
    );

    /// <summary>
    /// Applies one consumed batch: collapse the events into one pending state per key, embed the
    /// new aliases in one call, then run each table's MERGE. Safe to replay: a crash before the
    /// checkpoint only costs re-running the batch, never wrong rows. Returns how many aliases
    /// were written so the projector knows when to rebuild the alias search indexes.
    /// </summary>
    public async ValueTask<int> ProjectAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct = default) {
        var aliases  = new Dictionary<(string EntityId, string Alias), PendingAlias>();
        var mentions = new Dictionary<(string MemoryId, int SpanIndex), PendingMention>();

        foreach (var record in batch) {
            if (record.Value is not EntitiesMentioned resolved)
                continue;

            var position   = (long)record.LogPosition.CommitPosition!;
            var resolvedAt = KontextDataStore.EncodeTimestamp(resolved.ResolvedAt);

            for (var spanIndex = 0; spanIndex < resolved.Mentions.Count; spanIndex++) {
                var mention  = resolved.Mentions[spanIndex];
                var entityId = mention.EntityId;

                if (mention.OutcomeCase == EntityMention.OutcomeOneofCase.Created) {
                    var entity = mention.Created;
                    entityId = entity.EntityId;

                    foreach (var alias in entity.Aliases)
                        aliases[(entityId, alias)] = new PendingAlias(
                            entityId, entity.Type, alias, alias == entity.CanonicalName, resolvedAt, position);
                }

                mentions[(resolved.MemoryId, spanIndex)] = new PendingMention(
                    resolved.MemoryId, spanIndex, mention.SpanText, entityId,
                    (float)mention.Confidence, (int)mention.ResolvedBy, resolvedAt, position);
            }
        }

        if (aliases.Count == 0 && mentions.Count == 0)
            return 0;

        await EmbedAliases(aliases.Values, ct).ConfigureAwait(false);

        ApplyAliases(aliases.Values);
        ApplyMentions(mentions.Values);

        return aliases.Count;
    }

    // One embedding call for the whole batch — the events carry only the alias text; the
    // embeddings live in the read model.
    async ValueTask EmbedAliases(IReadOnlyCollection<PendingAlias> aliases, CancellationToken ct) {
        if (aliases.Count == 0)
            return;

        var pending = aliases.ToList();

        var generated = await embeddings
            .GenerateAsync(pending.Select(alias => alias.Alias), cancellationToken: ct)
            .ConfigureAwait(false);

        foreach (var (alias, embedding) in pending.Zip(generated))
            alias.Embed(embedding.Vector.ToArray());
    }

    void ApplyAliases(IReadOnlyCollection<PendingAlias> aliases) {
        if (aliases.Count == 0)
            return;

        var sql =
            $"""
             MERGE INTO ldb.main.entity_aliases AS t
             USING (SELECT
                 unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
                 unnest(CAST($entity_types AS VARCHAR[])) AS entity_type,
                 unnest(CAST($aliases AS VARCHAR[])) AS alias,
                 unnest(CAST($is_canonicals AS BOOLEAN[])) AS is_canonical,
                 unnest(CAST($created_ats AS BIGINT[])) AS created_at,
                 unnest(CAST($log_positions AS BIGINT[])) AS log_position,
                 unnest(CAST($embeddings AS FLOAT[][])) AS embedding_raw) AS s
             ON t.entity_id = s.entity_id AND t.alias = s.alias
             WHEN NOT MATCHED THEN INSERT (entity_id, entity_type, alias, is_canonical, created_at, log_position, embedding)
             VALUES (
                 s.entity_id, s.entity_type, s.alias, s.is_canonical, s.created_at, s.log_position,
                 CAST(s.embedding_raw AS FLOAT[{options.Dimensions}]))
             WHEN MATCHED THEN UPDATE SET
                 entity_type  = s.entity_type
               , is_canonical = s.is_canonical
               , embedding    = CAST(s.embedding_raw AS FLOAT[{options.Dimensions}])
               , log_position = s.log_position
             """;

        var count           = aliases.Count;
        var entityIds       = new List<string>(count);
        var entityTypes     = new List<string>(count);
        var aliasTexts      = new List<string>(count);
        var isCanonicals    = new List<bool>(count);
        var createdAts      = new List<long>(count);
        var logPositions    = new List<long>(count);
        var batchEmbeddings = new List<float[]>(count);

        foreach (var alias in aliases) {
            entityIds.Add(alias.EntityId);
            entityTypes.Add(alias.EntityType);
            aliasTexts.Add(alias.Alias);
            isCanonicals.Add(alias.IsCanonical);
            createdAts.Add(alias.CreatedAt);
            logPositions.Add(alias.LogPosition);
            batchEmbeddings.Add(alias.Embedding);
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new("entity_ids", entityIds));
        command.Parameters.Add(new("entity_types", entityTypes));
        command.Parameters.Add(new("aliases", aliasTexts));
        command.Parameters.Add(new("is_canonicals", isCanonicals));
        command.Parameters.Add(new("created_ats", createdAts));
        command.Parameters.Add(new("log_positions", logPositions));
        command.Parameters.Add(new("embeddings", batchEmbeddings));
        command.ExecuteNonQuery();
    }

    void ApplyMentions(IReadOnlyCollection<PendingMention> mentions) {
        if (mentions.Count == 0)
            return;

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
                unnest(CAST($linked_ats AS BIGINT[])) AS linked_at,
                unnest(CAST($log_positions AS BIGINT[])) AS log_position) AS s
            ON t.memory_id = s.memory_id AND t.span_index = s.span_index
            WHEN NOT MATCHED THEN INSERT (
                memory_id, span_index, span_text, entity_id,
                confidence, resolved_by, linked_at, log_position)
            VALUES (
                s.memory_id, s.span_index, s.span_text, s.entity_id,
                s.confidence, s.resolved_by, s.linked_at, s.log_position)
            WHEN MATCHED THEN UPDATE SET
                span_text    = s.span_text
              , entity_id    = s.entity_id
              , confidence   = s.confidence
              , resolved_by  = s.resolved_by
              , linked_at    = s.linked_at
              , log_position = s.log_position
            """;

        var count        = mentions.Count;
        var memoryIds    = new List<string>(count);
        var spanIndexes  = new List<int>(count);
        var spanTexts    = new List<string>(count);
        var entityIds    = new List<string>(count);
        var confidences  = new List<float>(count);
        var resolvedBys  = new List<int>(count);
        var linkedAts    = new List<long>(count);
        var logPositions = new List<long>(count);

        foreach (var mention in mentions) {
            memoryIds.Add(mention.MemoryId);
            spanIndexes.Add(mention.SpanIndex);
            spanTexts.Add(mention.SpanText);
            entityIds.Add(mention.EntityId);
            confidences.Add(mention.Confidence);
            resolvedBys.Add(mention.ResolvedBy);
            linkedAts.Add(mention.LinkedAt);
            logPositions.Add(mention.LogPosition);
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new("memory_ids", memoryIds));
        command.Parameters.Add(new("span_indexes", spanIndexes));
        command.Parameters.Add(new("span_texts", spanTexts));
        command.Parameters.Add(new("entity_ids", entityIds));
        command.Parameters.Add(new("confidences", confidences));
        command.Parameters.Add(new("resolved_bys", resolvedBys));
        command.Parameters.Add(new("linked_ats", linkedAts));
        command.Parameters.Add(new("log_positions", logPositions));
        command.ExecuteNonQuery();
    }
}
