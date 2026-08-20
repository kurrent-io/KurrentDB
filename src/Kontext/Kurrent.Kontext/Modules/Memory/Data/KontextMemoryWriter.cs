// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Contracts.V3.Memory;
using Kurrent.Kontext.Data;
using Kurrent.Quack;
using Kurrent.Surge;
using Microsoft.Extensions.AI;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Modules.Memory.Data;

/// <summary>
/// The memories read model's batch writer: applies one consumed batch of memory events as one
/// facet-guarded MERGE — one lance commit and one row-write per touched id, safe to replay.
/// Runs on the caller's connection; the projector owns the connection, the transaction scope,
/// and the checkpoint. Not thread safe: one consumer loop drives it, batch by batch.
/// </summary>
public sealed class KontextMemoryWriter(
    DuckDBAdvancedConnection connection,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EmbeddingGenerationOptions options
) {

    // The pending write-state for one memory id, folded from every batch event that touched it:
    // an optional retain (the row body) plus the optional lifecycle marks — a facet left null
    // simply sits out its leg. LogPosition is the LAST event that touched the id, which is the
    // value the row's stamp converges to anyway.
    sealed class PendingMemory(string memoryId) {
        public string MemoryId { get; } = memoryId;

        public MemoryContracts.Memory? Memory       { get; private set; }
        public long              RetainedAt   { get; private set; }
        public string?           SupersededBy { get; private set; }
        public long              SupersededAt { get; private set; }
        public long?             RetractedAt  { get; private set; }
        public long?             RecalledAt   { get; private set; }
        public long              LogPosition  { get; private set; }

        // The batch-computed embedding for a retained body; fold-only entries keep the empty
        // embedding the statement never reads.
        public float[] Embedding { get; private set; } = [];

        public PendingMemory Touch(long position) {
            LogPosition = position;
            return this;
        }

        public void Retain(MemoryContracts.Memory memory, long retainedAt) {
            Memory     = memory;
            RetainedAt = retainedAt;
        }

        public void Supersede(string supersededBy, long supersededAt) {
            SupersededBy = supersededBy;
            SupersededAt = supersededAt;
        }

        public void Retract(long retractedAt) => RetractedAt = retractedAt;

        public void Recall(long recalledAt) => RecalledAt = recalledAt;

        public void Embed(float[] embedding) => Embedding = embedding;
    }

    /// <summary>
    /// Applies one consumed batch: aggregate to one pending state per id, batch-embed the
    /// retained bodies, then apply everything as one MERGE. Safe to replay the whole batch —
    /// the upsert is partitioned and the folds set terminal values, so a crash between this and
    /// the checkpoint only costs re-execution, never state.
    /// </summary>
    public async ValueTask ProjectAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct = default) {
        // Aggregation folds the batch into ONE pending state per memory id BEFORE touching the
        // engine — facets last-write-win in log order, which keeps the MERGE's unnested source
        // free of duplicate keys and makes the latest recall set the recency clock.
        var pending = new Dictionary<string, PendingMemory>();

        foreach (var record in batch) {
            var position = (long)record.LogPosition.CommitPosition!;

            switch (record.Value) {
                case MemoriesRetained retained: {
                    // One event carries a whole retain call — one row per memory.
                    var retainedAt = KontextDataStore.EncodeTimestamp(retained.RetainedAt);

                    foreach (var entry in retained.Memories) {
                        Touch(entry.MemoryId, position).Retain(entry.Memory, retainedAt);

                        // Supersession is set at Retain — fold it into the old rows even when
                        // they land in this same batch.
                        foreach (var supersededId in entry.Memory.Supersedes)
                            Touch(supersededId, position).Supersede(entry.MemoryId, retainedAt);
                    }

                    break;
                }

                case MemoryRetracted retraction: {
                    var retractedAt = KontextDataStore.EncodeTimestamp(retraction.RetractedAt);
                    foreach (var memoryId in retraction.RetractedMemoryIds)
                        Touch(memoryId, position).Retract(retractedAt);

                    break;
                }

                case MemoriesRecalled recall: {
                    // Reconsolidation: a recall IS an access — the recency clock resets.
                    var recalledAt = KontextDataStore.EncodeTimestamp(recall.RecalledAt);
                    foreach (var scored in recall.Memories)
                        Touch(scored.MemoryId, position).Recall(recalledAt);

                    break;
                }
            }
        }

        if (pending.Count == 0)
            return;

        // One model call for the whole batch — the events carry content only, the read model
        // owns the embeddings, and per-event embedding is the projection path's dominant cost.
        // Only retained bodies embed; each embedding lands on its own pending state.
        var retainedMemories = pending.Values.Where(memory => memory.Memory is not null).ToList();

        if (retainedMemories.Count > 0) {
            var generated = await embeddings
                .GenerateAsync(retainedMemories.Select(memory => memory.Memory!.Content), cancellationToken: ct)
                .ConfigureAwait(false);

            foreach (var (memory, embedding) in retainedMemories.Zip(generated))
                memory.Embed(embedding.Vector.ToArray());
        }

        Apply(pending.Values);

        return;

        PendingMemory Touch(string memoryId, long position) {
            if (!pending.TryGetValue(memoryId, out var memory))
                pending[memoryId] = memory = new(memoryId);

            return memory.Touch(position);
        }
    }

    // Upsert with partitioned column ownership. The server mints every memory_id, so a matched
    // row with a body can only mean replay — and replay must work in place:
    // - the retain facet owns the content columns: the matched arm refreshes exactly those when
    //   the batch retained the id (a full replay rewrites content and embeddings) and keeps the
    //   target's otherwise
    // - the fold facets own the lifecycle columns: flags OR in, timestamps coalesce in — a
    //   replayed retain cannot resurrect a memory that later events retracted or superseded
    //
    // The insert arm writes the terminal batch state directly: a memory retained and retracted
    // in one batch is born retracted. last_accessed_at seeds to retained_at at birth unless the
    // same batch already recalled it; on match a retain never touches the recency clock — only
    // recalls advance it. A fold-only source row that matches nothing does nothing: the
    // NOT MATCHED arm is guarded on the retain facet.
    void Apply(IReadOnlyCollection<PendingMemory> memories) {
        var sql =
            $"""
             MERGE INTO ldb.main.memories AS t
             USING (SELECT
                 unnest(CAST($memory_ids AS VARCHAR[])) AS memory_id,
                 unnest(CAST($retained AS BOOLEAN[])) AS retained,
                 unnest(CAST($memory_types AS INTEGER[])) AS memory_type,
                 unnest(CAST($contents AS VARCHAR[])) AS content,
                 unnest(CAST($importances AS INTEGER[])) AS importance,
                 unnest(CAST($tags AS VARCHAR[][])) AS tags,
                 unnest(CAST($reasonings AS VARCHAR[])) AS reasoning,
                 unnest(CAST($evidence AS VARCHAR[][])) AS evidence,
                 unnest(CAST($cited_memory_ids AS VARCHAR[][])) AS cited_memory_ids,
                 unnest(CAST($supersedes AS VARCHAR[][])) AS supersedes,
                 unnest(CAST($validity_starts AS BIGINT[])) AS validity_start,
                 unnest(CAST($validity_ends AS BIGINT[])) AS validity_end,
                 unnest(CAST($retained_ats AS BIGINT[])) AS retained_at,
                 unnest(CAST($superseded_bys AS VARCHAR[])) AS superseded_by,
                 unnest(CAST($superseded_ats AS BIGINT[])) AS superseded_at,
                 unnest(CAST($retracted_ats AS BIGINT[])) AS retracted_at,
                 unnest(CAST($recalled_ats AS BIGINT[])) AS recalled_at,
                 unnest(CAST($log_positions AS BIGINT[])) AS log_position,
                 unnest(CAST($embeddings AS FLOAT[][])) AS embedding_raw) AS s
             ON t.memory_id = s.memory_id
             WHEN NOT MATCHED AND s.retained THEN INSERT (
                 memory_id, memory_type, content, importance,
                 tags, reasoning, evidence, cited_memory_ids, supersedes,
                 validity_start, validity_end, retained_at, last_accessed_at,
                 is_retracted, retracted_at, is_superseded, superseded_at, superseded_by,
                 log_position, embedding)
             VALUES (
                 s.memory_id, s.memory_type, s.content, s.importance,
                 s.tags, s.reasoning, s.evidence, s.cited_memory_ids, s.supersedes,
                 s.validity_start, s.validity_end, s.retained_at,
                 coalesce(s.recalled_at, s.retained_at),
                 s.retracted_at IS NOT NULL, s.retracted_at,
                 s.superseded_by IS NOT NULL, s.superseded_at, coalesce(s.superseded_by, ''),
                 s.log_position,
                 CASE WHEN s.retained THEN CAST(s.embedding_raw AS FLOAT[{options.Dimensions}]) END)
             WHEN MATCHED THEN UPDATE SET
                 memory_type      = CASE WHEN s.retained THEN s.memory_type ELSE t.memory_type END
               , content          = CASE WHEN s.retained THEN s.content ELSE t.content END
               , importance       = CASE WHEN s.retained THEN s.importance ELSE t.importance END
               , tags             = CASE WHEN s.retained THEN s.tags ELSE t.tags END
               , reasoning        = CASE WHEN s.retained THEN s.reasoning ELSE t.reasoning END
               , evidence         = CASE WHEN s.retained THEN s.evidence ELSE t.evidence END
               , cited_memory_ids = CASE WHEN s.retained THEN s.cited_memory_ids ELSE t.cited_memory_ids END
               , supersedes       = CASE WHEN s.retained THEN s.supersedes ELSE t.supersedes END
               , validity_start   = CASE WHEN s.retained THEN s.validity_start ELSE t.validity_start END
               , validity_end     = CASE WHEN s.retained THEN s.validity_end ELSE t.validity_end END
               , retained_at      = CASE WHEN s.retained THEN s.retained_at ELSE t.retained_at END
               , embedding        = CASE WHEN s.retained THEN CAST(s.embedding_raw AS FLOAT[{options.Dimensions}]) ELSE t.embedding END
               , last_accessed_at = coalesce(s.recalled_at, t.last_accessed_at)
               , is_retracted     = t.is_retracted OR s.retracted_at IS NOT NULL
               , retracted_at     = coalesce(s.retracted_at, t.retracted_at)
               , is_superseded    = t.is_superseded OR s.superseded_by IS NOT NULL
               , superseded_at    = coalesce(s.superseded_at, t.superseded_at)
               , superseded_by    = coalesce(s.superseded_by, t.superseded_by)
               , log_position     = s.log_position
             """;

        var count           = memories.Count;
        var memoryIds       = new List<string>(count);
        var retainedFlags   = new List<bool>(count);
        var memoryTypes     = new List<int>(count);
        var contents        = new List<string?>(count);
        var importances     = new List<int>(count);
        var tags            = new List<List<string>>(count);
        var reasonings      = new List<string?>(count);
        var evidence        = new List<List<string>>(count);
        var citedMemoryIds  = new List<List<string>>(count);
        var supersedes      = new List<List<string>>(count);
        var validityStarts  = new List<long?>(count);
        var validityEnds    = new List<long?>(count);
        var retainedAts     = new List<long?>(count);
        var supersededBys   = new List<string?>(count);
        var supersededAts   = new List<long?>(count);
        var retractedAts    = new List<long?>(count);
        var recalledAts     = new List<long?>(count);
        var logPositions    = new List<long>(count);
        var batchEmbeddings = new List<float[]>(count);

        // The id lives on the event, not on the memory body: the server mints it. This unrolled
        // row IS the v2 MemoryRowArgs — the appender binder consumes the same values in the same
        // schema order. A fold-only entry carries neutral body values the statement never reads.
        foreach (var pendingMemory in memories) {
            var memory = pendingMemory.Memory;

            memoryIds.Add(pendingMemory.MemoryId);
            retainedFlags.Add(memory is not null);
            memoryTypes.Add(memory is not null ? (int)memory.MemoryType : 0);
            contents.Add(memory?.Content);
            importances.Add(memory is not null ? (int)memory.Importance : 0);
            tags.Add(memory?.Tags.Select(KontextDataStore.EncodeTag).ToList() ?? []);
            reasonings.Add(memory?.Reasoning);
            evidence.Add(memory?.Evidence.Select(KontextDataStore.EncodeEvidence).ToList() ?? []);
            citedMemoryIds.Add(memory is not null ? KontextDataStore.EncodeCitedMemoryIds(memory) : []);
            supersedes.Add(memory?.Supersedes.ToList() ?? []);
            validityStarts.Add(memory?.Validity?.PerceivedStart is { } start ? KontextDataStore.EncodeTimestamp(start) : null);
            validityEnds.Add(memory?.Validity?.PerceivedEnd is { } end ? KontextDataStore.EncodeTimestamp(end) : null);
            retainedAts.Add(memory is not null ? pendingMemory.RetainedAt : null);
            supersededBys.Add(pendingMemory.SupersededBy);
            supersededAts.Add(pendingMemory.SupersededBy is not null ? pendingMemory.SupersededAt : null);
            retractedAts.Add(pendingMemory.RetractedAt);
            recalledAts.Add(pendingMemory.RecalledAt);
            logPositions.Add(pendingMemory.LogPosition);
            batchEmbeddings.Add(pendingMemory.Embedding);
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new("memory_ids", memoryIds));
        command.Parameters.Add(new("retained", retainedFlags));
        command.Parameters.Add(new("memory_types", memoryTypes));
        command.Parameters.Add(new("contents", contents));
        command.Parameters.Add(new("importances", importances));
        command.Parameters.Add(new("tags", tags));
        command.Parameters.Add(new("reasonings", reasonings));
        command.Parameters.Add(new("evidence", evidence));
        command.Parameters.Add(new("cited_memory_ids", citedMemoryIds));
        command.Parameters.Add(new("supersedes", supersedes));
        command.Parameters.Add(new("validity_starts", validityStarts));
        command.Parameters.Add(new("validity_ends", validityEnds));
        command.Parameters.Add(new("retained_ats", retainedAts));
        command.Parameters.Add(new("superseded_bys", supersededBys));
        command.Parameters.Add(new("superseded_ats", supersededAts));
        command.Parameters.Add(new("retracted_ats", retractedAts));
        command.Parameters.Add(new("recalled_ats", recalledAts));
        command.Parameters.Add(new("log_positions", logPositions));
        command.Parameters.Add(new("embeddings", batchEmbeddings));
        command.ExecuteNonQuery();
    }

}
