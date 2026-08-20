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
/// When entities merge, the loser's merged_into always points straight at the final survivor,
/// whether the loser's rows landed in this batch, were already stored, or the survivor itself
/// lost an earlier batch, so entity reads never follow pointer chains. Mentions are the
/// deliberate exception: they keep the entity id resolution produced, and readers reach the
/// survivor through the one merged_into hop.
/// </summary>
public sealed class KontextEntityWriter(
    DuckDBAdvancedConnection connection,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EmbeddingGenerationOptions options
) {
    sealed class PendingEntity(string entityId) {
        public string EntityId { get; } = entityId;

        public bool    IsNew         { get; private set; }
        public string? EntityType    { get; private set; }
        public string? CanonicalName { get; private set; }
        public long    CreatedAt     { get; private set; }
        public string? MergedInto    { get; private set; }
        public long    MergedAt      { get; private set; }
        public long    LogPosition   { get; private set; }

        public PendingEntity Touch(long position) {
            LogPosition = position;
            return this;
        }

        public void Create(string entityType, string canonicalName, long createdAt) {
            IsNew         = true;
            EntityType    = entityType;
            CanonicalName = canonicalName;
            CreatedAt     = createdAt;
        }

        public void Merge(string mergedInto, long mergedAt) {
            MergedInto = mergedInto;
            MergedAt   = mergedAt;
        }

        public void Repoint(string terminal) => MergedInto = terminal;
    }

    sealed class PendingAlias(string entityId, string alias, long createdAt, long logPosition) {
        public string EntityId    { get; } = entityId;
        public string Alias       { get; } = alias;
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
    /// Applies one consumed batch: collapse the events into one pending state per key, repoint
    /// merge chains through the batch and the stored pointers of its survivors, embed the new
    /// aliases in one call, then run each table's MERGE. Safe to
    /// replay: a crash before the checkpoint only costs re-running the batch, never wrong rows.
    /// Returns how many aliases were written so the projector knows when to rebuild the alias
    /// search indexes.
    /// </summary>
    public async ValueTask<int> ProjectAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct = default) {
        var entities = new Dictionary<string, PendingEntity>();
        var aliases  = new Dictionary<(string EntityId, string Alias), PendingAlias>();
        var mentions = new Dictionary<(string MemoryId, int SpanIndex), PendingMention>();

        foreach (var record in batch) {
            var position = (long)record.LogPosition.CommitPosition!;

            switch (record.Value) {
                case EntitiesMentioned resolved: {
                    var resolvedAt = KontextDataStore.EncodeTimestamp(resolved.ResolvedAt);

                    for (var spanIndex = 0; spanIndex < resolved.Mentions.Count; spanIndex++) {
                        var mention  = resolved.Mentions[spanIndex];
                        var entityId = mention.EntityId;

                        if (mention.OutcomeCase == EntityMention.OutcomeOneofCase.Created) {
                            var entity = mention.Created;
                            entityId = entity.EntityId;

                            Touch(entityId, position).Create(entity.Type, entity.CanonicalName, resolvedAt);

                            foreach (var alias in entity.Aliases)
                                aliases[(entityId, alias)] = new PendingAlias(entityId, alias, resolvedAt, position);
                        }

                        mentions[(resolved.MemoryId, spanIndex)] = new PendingMention(
                            resolved.MemoryId, spanIndex, mention.SpanText, entityId,
                            (float)mention.Confidence, (int)mention.ResolvedBy, resolvedAt, position);
                    }

                    break;
                }

                case EntitiesMerged merged: {
                    var mergedAt = KontextDataStore.EncodeTimestamp(merged.MergedAt);

                    foreach (var merge in merged.Merges)
                        Touch(merge.EntityId, position).Merge(merge.MergedInto, mergedAt);

                    break;
                }
            }
        }

        if (entities.Count == 0 && mentions.Count == 0)
            return 0;

        RepointMerges(entities, LoadStoredRedirects(entities.Values));

        await EmbedAliases(aliases.Values, ct).ConfigureAwait(false);

        ApplyEntities(entities.Values);
        RepointStoredMerges(entities.Values);
        ApplyAliases(aliases.Values);
        ApplyMentions(mentions.Values);

        return aliases.Count;

        PendingEntity Touch(string entityId, long position) {
            if (!entities.TryGetValue(entityId, out var entity))
                entities[entityId] = entity = new(entityId);

            return entity.Touch(position);
        }
    }

    // A merge can land on a survivor that already lost an earlier batch — the batch alone
    // cannot see that. Loads the stored pointer of every batch survivor so the repoint walk
    // still ends on the final survivor.
    Dictionary<string, string> LoadStoredRedirects(IReadOnlyCollection<PendingEntity> entities) {
        var survivors = entities
            .Where(entity => entity.MergedInto is not null)
            .Select(entity => entity.MergedInto!)
            .Distinct()
            .ToList();

        if (survivors.Count == 0)
            return [];

        const string sql =
            """
            SELECT entity_id, merged_into
            FROM ldb.main.entities
            WHERE merged_into IS NOT NULL
              AND array_contains(CAST($entity_ids AS VARCHAR[]), entity_id)
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new("entity_ids", survivors));

        var redirects = new Dictionary<string, string>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            redirects[reader.GetString(0)] = reader.GetString(1);

        return redirects;
    }

    // Walks each loser's chain to its final survivor — this batch's merges first (they are
    // newer than anything stored), then the stored pointers — and points the loser straight at
    // it. The hop bound stops a cyclic merge from looping forever.
    static void RepointMerges(Dictionary<string, PendingEntity> entities, Dictionary<string, string> storedRedirects) {
        var maxHops = entities.Count + storedRedirects.Count;

        foreach (var entity in entities.Values.Where(entity => entity.MergedInto is not null)) {
            var terminal = entity.MergedInto!;

            for (var hop = 0; hop < maxHops; hop++) {
                if (entities.TryGetValue(terminal, out var next) && next.MergedInto is not null)
                    terminal = next.MergedInto;
                else if (storedRedirects.TryGetValue(terminal, out var stored))
                    terminal = stored;
                else
                    break;
            }

            entity.Repoint(terminal);
        }
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

    // Create and merge each own their columns: a create writes the body columns, a merge the
    // lifecycle columns. A replayed create cannot resurrect a merged entity, and a merge for
    // an id with no row does nothing.
    void ApplyEntities(IReadOnlyCollection<PendingEntity> entities) {
        if (entities.Count == 0)
            return;

        const string sql =
            """
            MERGE INTO ldb.main.entities AS t
            USING (SELECT
                unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
                unnest(CAST($is_new AS BOOLEAN[])) AS is_new,
                unnest(CAST($entity_types AS VARCHAR[])) AS entity_type,
                unnest(CAST($canonical_names AS VARCHAR[])) AS canonical_name,
                unnest(CAST($created_ats AS BIGINT[])) AS created_at,
                unnest(CAST($merged_intos AS VARCHAR[])) AS merged_into,
                unnest(CAST($merged_ats AS BIGINT[])) AS merged_at,
                unnest(CAST($log_positions AS BIGINT[])) AS log_position) AS s
            ON t.entity_id = s.entity_id
            WHEN NOT MATCHED AND s.is_new THEN INSERT (
                entity_id, entity_type, canonical_name,
                merged_into, merged_at, created_at, log_position)
            VALUES (
                s.entity_id, s.entity_type, s.canonical_name,
                s.merged_into, s.merged_at, s.created_at, s.log_position)
            WHEN MATCHED THEN UPDATE SET
                entity_type    = CASE WHEN s.is_new THEN s.entity_type ELSE t.entity_type END
              , canonical_name = CASE WHEN s.is_new THEN s.canonical_name ELSE t.canonical_name END
              , created_at     = CASE WHEN s.is_new THEN s.created_at ELSE t.created_at END
              , merged_into    = coalesce(s.merged_into, t.merged_into)
              , merged_at      = coalesce(s.merged_at, t.merged_at)
              , log_position   = s.log_position
            """;

        var count          = entities.Count;
        var entityIds      = new List<string>(count);
        var isNew          = new List<bool>(count);
        var entityTypes    = new List<string?>(count);
        var canonicalNames = new List<string?>(count);
        var createdAts     = new List<long?>(count);
        var mergedIntos    = new List<string?>(count);
        var mergedAts      = new List<long?>(count);
        var logPositions   = new List<long>(count);

        foreach (var entity in entities) {
            entityIds.Add(entity.EntityId);
            isNew.Add(entity.IsNew);
            entityTypes.Add(entity.EntityType);
            canonicalNames.Add(entity.CanonicalName);
            createdAts.Add(entity.IsNew ? entity.CreatedAt : (long?)null);
            mergedIntos.Add(entity.MergedInto);
            mergedAts.Add(entity.MergedInto is not null ? entity.MergedAt : (long?)null);
            logPositions.Add(entity.LogPosition);
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new("entity_ids", entityIds));
        command.Parameters.Add(new("is_new", isNew));
        command.Parameters.Add(new("entity_types", entityTypes));
        command.Parameters.Add(new("canonical_names", canonicalNames));
        command.Parameters.Add(new("created_ats", createdAts));
        command.Parameters.Add(new("merged_intos", mergedIntos));
        command.Parameters.Add(new("merged_ats", mergedAts));
        command.Parameters.Add(new("log_positions", logPositions));
        command.ExecuteNonQuery();
    }

    // Fixes rows already in the table: anything pointing at an entity that lost in this batch
    // is repointed to its survivor. Runs after ApplyEntities so this batch's own inserts get
    // fixed too.
    void RepointStoredMerges(IReadOnlyCollection<PendingEntity> entities) {
        var merges = entities.Where(entity => entity.MergedInto is not null).ToList();

        if (merges.Count == 0)
            return;

        const string sql =
            """
            MERGE INTO ldb.main.entities AS t
            USING (SELECT
                unnest(CAST($losers AS VARCHAR[])) AS loser,
                unnest(CAST($survivors AS VARCHAR[])) AS survivor,
                unnest(CAST($log_positions AS BIGINT[])) AS log_position) AS s
            ON t.merged_into = s.loser
            WHEN MATCHED THEN UPDATE SET
                merged_into  = s.survivor
              , log_position = s.log_position
            """;

        var losers       = new List<string>(merges.Count);
        var survivors    = new List<string>(merges.Count);
        var logPositions = new List<long>(merges.Count);

        foreach (var merge in merges) {
            losers.Add(merge.EntityId);
            survivors.Add(merge.MergedInto!);
            logPositions.Add(merge.LogPosition);
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new("losers", losers));
        command.Parameters.Add(new("survivors", survivors));
        command.Parameters.Add(new("log_positions", logPositions));
        command.ExecuteNonQuery();
    }

    void ApplyAliases(IReadOnlyCollection<PendingAlias> aliases) {
        if (aliases.Count == 0)
            return;

        var sql =
            $"""
             MERGE INTO ldb.main.entity_aliases AS t
             USING (SELECT
                 unnest(CAST($entity_ids AS VARCHAR[])) AS entity_id,
                 unnest(CAST($aliases AS VARCHAR[])) AS alias,
                 unnest(CAST($created_ats AS BIGINT[])) AS created_at,
                 unnest(CAST($log_positions AS BIGINT[])) AS log_position,
                 unnest(CAST($embeddings AS FLOAT[][])) AS embedding_raw) AS s
             ON t.entity_id = s.entity_id AND t.alias = s.alias
             WHEN NOT MATCHED THEN INSERT (entity_id, alias, created_at, log_position, embedding)
             VALUES (
                 s.entity_id, s.alias, s.created_at, s.log_position,
                 CAST(s.embedding_raw AS FLOAT[{options.Dimensions}]))
             WHEN MATCHED THEN UPDATE SET
                 embedding    = CAST(s.embedding_raw AS FLOAT[{options.Dimensions}])
               , log_position = s.log_position
             """;

        var count           = aliases.Count;
        var entityIds       = new List<string>(count);
        var aliasTexts      = new List<string>(count);
        var createdAts      = new List<long>(count);
        var logPositions    = new List<long>(count);
        var batchEmbeddings = new List<float[]>(count);

        foreach (var alias in aliases) {
            entityIds.Add(alias.EntityId);
            aliasTexts.Add(alias.Alias);
            createdAts.Add(alias.CreatedAt);
            logPositions.Add(alias.LogPosition);
            batchEmbeddings.Add(alias.Embedding);
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new("entity_ids", entityIds));
        command.Parameters.Add(new("aliases", aliasTexts));
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
