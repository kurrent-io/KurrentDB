// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable VirtualMemberCallInConstructor

using Google.Protobuf;
using Kurrent.Kontext.Contracts;
using Kurrent.Kontext.Data;
using Kurrent.Surge.Consumers;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Modules.Memory.Data;

public sealed class KontextMemoryProjection : KontextProjection {
    public override ConsumeFilter Filter => KontextConventions.Filters.MemoriesFilter;

    public KontextMemoryProjection(IEmbeddingGenerator<string, Embedding<float>> embeddings) {
        Project<MemoriesRetained>(async (msg, db, ctx) => {
            if (msg.Memories.Count == 0)
                return;

            var retainedAt = KontextDataStore.EncodeTimestamp(msg.RetainedAt);

            // The event carries content only — the read model owns the vector, so the
            // projection embeds at apply time with the same generator the retrieval pipeline
            // queries with (the FLOAT[N] column dimension must match its model). ONE call for the
            // whole batch: generators charge and round-trip per request, not per input.
            var vectors = await embeddings
                .GenerateAsync(msg.Memories.Select(retained => retained.Memory.Content), cancellationToken: ctx.CancellationToken)
                .ConfigureAwait(false);

            // Every vector shares the model's dimension, so the FLOAT[N] type is fixed for the batch.
            var dimension = vectors[0].Vector.Length;

            using var scope = db.GetScopedConnection(out var connection);

            // ONE transaction for the batch, which is ONE storage commit — the unit of the retain
            // call. Per-memory transactions would multiply commits by the batch size (see
            // AppenderLanceProbeTests, which pins that commits scale with flushes, not rows).
            using var tx = connection.BeginTransaction();

            // Insert-if-absent, deliberately with NO WHEN MATCHED arm. The server mints every
            // memory_id, so a matched row can only mean replay after a crash — and the row may
            // already carry lifecycle folded by LATER events in the replayed tail (a retraction,
            // a supersession); an overwrite would briefly resurrect the memory until replay
            // re-reaches those events. Source-select shape
            // with explicit casts on list/vector items mirrors DuckDBSqlComposer; the FLOAT[N]
            // dimension is part of the TYPE and can never bind (the schema DDL rule), so it
            // interpolates from the vector actually produced.
            //
            // last_accessed_at seeds to retained_at: the recency clock starts at retention
            // and only MemoriesRecalled advances it. retracted/superseded timestamps stay
            // NULL until their events fold them in; the flags write as false, absent
            // supersession as '' and absent evidence as an empty list — the store's readers
            // and filters never reason about NULL booleans, strings, or lists.
            var insertMemorySql =
                $"""
                MERGE INTO ldb.main.memories AS t
                USING (SELECT
                    $memory_id AS memory_id,
                    $memory_type AS memory_type,
                    $content AS content,
                    $importance AS importance,
                    CAST($tags AS VARCHAR[]) AS tags,
                    $reasoning AS reasoning,
                    CAST($evidence AS VARCHAR[]) AS evidence,
                    CAST($cited_memory_ids AS VARCHAR[]) AS cited_memory_ids,
                    CAST($supersedes AS VARCHAR[]) AS supersedes,
                    $validity_start AS validity_start,
                    $validity_end AS validity_end,
                    $retained_at AS retained_at,
                    $retained_at AS last_accessed_at,
                    false AS is_retracted,
                    false AS is_superseded,
                    '' AS superseded_by,
                    $log_position AS log_position,
                    CAST($embedding AS FLOAT[{dimension}]) AS embedding) AS s
                ON t.memory_id = s.memory_id
                WHEN NOT MATCHED THEN INSERT (
                    memory_id, memory_type, content, importance,
                    tags, reasoning, evidence, cited_memory_ids, supersedes,
                    validity_start, validity_end, retained_at, last_accessed_at,
                    is_retracted, is_superseded, superseded_by, log_position, embedding)
                VALUES (
                    s.memory_id, s.memory_type, s.content, s.importance,
                    s.tags, s.reasoning, s.evidence, s.cited_memory_ids, s.supersedes,
                    s.validity_start, s.validity_end, s.retained_at, s.last_accessed_at,
                    s.is_retracted, s.is_superseded, s.superseded_by, s.log_position, s.embedding)
                """;

            // Applied in REQUEST ORDER, which is the order the ids were assigned. It matters within
            // a batch: a memory may supersede one retained earlier in the same call, and that row
            // has to exist before the fold reaches for it.
            for (var index = 0; index < msg.Memories.Count; index++) {
                var retained = msg.Memories[index];
                var memory   = retained.Memory;

                await using (var command = connection.CreateCommand()) {
                    command.CommandText = insertMemorySql;
                    // The id lives on the event, not on the memory body: the write shape carries none
                    // because the server mints it (see resources.proto Memory).
                    command.Parameters.Add(new("memory_id", retained.MemoryId));
                    command.Parameters.Add(new("memory_type", (int)memory.MemoryType));
                    command.Parameters.Add(new("content", memory.Content));
                    command.Parameters.Add(new("importance", (int)memory.Importance));
                    command.Parameters.Add(new("tags", memory.Tags.Select(KontextDataStore.EncodeTag).ToList()));
                    command.Parameters.Add(new("reasoning", memory.Reasoning));
                    command.Parameters.Add(new("evidence", memory.Evidence.Select(KontextDataStore.EncodeEvidence).ToList()));
                    command.Parameters.Add(new("cited_memory_ids", KontextDataStore.EncodeCitedMemoryIds(memory)));
                    command.Parameters.Add(new("supersedes", memory.Supersedes.ToList()));
                    command.Parameters.Add(new("validity_start", KontextDataStore.EncodeOptionalTimestamp(memory.Validity?.PerceivedStart)));
                    command.Parameters.Add(new("validity_end", KontextDataStore.EncodeOptionalTimestamp(memory.Validity?.PerceivedEnd)));
                    command.Parameters.Add(new("retained_at", retainedAt));
                    command.Parameters.Add(new("log_position", ctx.Record.LogPosition.CommitPosition));
                    command.Parameters.Add(new("embedding", vectors[index].Vector.ToArray()));
                    command.ExecuteNonQuery();
                }

                // A retained memory that supersedes others folds that fact into the old rows in
                // the same transaction — supersession is set at Retain, there is no separate
                // supersede operation (see resources.proto Memory.supersedes). MERGE, not UPDATE:
                // lance rejects filtered updates (see the class doc); a matched-only MERGE keyed
                // on the unnested id list is the engine-supported equivalent, and ids that match
                // no row simply fold nothing.
                if (memory.Supersedes.Count == 0)
                    continue;

                const string foldSupersededSql =
                    """
                    MERGE INTO ldb.main.memories AS t
                    USING (SELECT unnest($superseded_ids) AS memory_id) AS s
                    ON t.memory_id = s.memory_id
                    WHEN MATCHED THEN UPDATE SET
                        is_superseded = true
                      , superseded_at = $superseded_at
                      , superseded_by = $superseded_by
                      , log_position  = $log_position
                    """;

                using var fold = connection.CreateCommand();
                fold.CommandText = foldSupersededSql;
                fold.Parameters.Add(new("superseded_by", retained.MemoryId));
                fold.Parameters.Add(new("superseded_at", retainedAt));
                fold.Parameters.Add(new("superseded_ids", memory.Supersedes.ToList()));
                fold.Parameters.Add(new("log_position", ctx.Record.LogPosition.CommitPosition));
                fold.ExecuteNonQuery();
            }

            tx.CommitOnDispose();
        });

        Project<MemoryRetracted>((msg, db, ctx) => {
            // Matched-only MERGE — lance rejects filtered updates (see the class doc).
            const string sql =
                """
                MERGE INTO ldb.main.memories AS t
                USING (SELECT unnest($retracted_ids) AS memory_id) AS s
                ON t.memory_id = s.memory_id
                WHEN MATCHED THEN UPDATE SET
                    is_retracted = true
                  , retracted_at = $retracted_at
                  , log_position = $log_position
                """;

            using var scope = db.GetScopedConnection(out var connection);

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            // retracted_memory_ids already carries memory_id plus every cascaded derived memory.
            command.Parameters.Add(new("retracted_ids", msg.RetractedMemoryIds.ToList()));
            command.Parameters.Add(new("retracted_at", KontextDataStore.EncodeTimestamp(msg.RetractedAt)));
            command.Parameters.Add(new("log_position", ctx.Record.LogPosition.CommitPosition));
            command.ExecuteNonQuery();

            return ValueTask.CompletedTask;
        });

        Project<MemoriesRecalled>((msg, db, ctx) => {
            // Matched-only MERGE — lance rejects filtered updates (see the class doc).
            const string sql =
                """
                MERGE INTO ldb.main.memories AS t
                USING (SELECT unnest($memory_ids) AS memory_id) AS s
                ON t.memory_id = s.memory_id
                WHEN MATCHED THEN UPDATE SET
                    last_accessed_at = $recalled_at
                  , log_position     = $log_position
                """;

            using var scope = db.GetScopedConnection(out var connection);

            // Reconsolidation: a recall IS an access, so every returned memory's recency
            // clock resets to recalled_at — the load-bearing half of the temporal-decay
            // model (see the MemoriesRecalled contract doc in events.proto).
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new("memory_ids", msg.Memories.Select(scored => scored.MemoryId).ToList()));
            command.Parameters.Add(new("recalled_at", KontextDataStore.EncodeTimestamp(msg.RecalledAt)));
            command.Parameters.Add(new("log_position", ctx.Record.LogPosition.CommitPosition));
            command.ExecuteNonQuery();

            return ValueTask.CompletedTask;
        });

        // Deliberately not handled yet:
        // - ReflectionCompleted: superseded_by is a scalar, but the event carries parallel id
        //   arrays — the synthesized→superseded mapping rule is an open contract question.
        // - MemoriesAccessed: the contract now carries `accessed_at` and states that reclaim
        //   refreshes the recency clock, so this fold is buildable — it is simply not built.
        //   Until it is, only MemoriesRecalled advances last_accessed_at.
    }


}
