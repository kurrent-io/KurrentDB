// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using Kurrent.Surge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Records.Data;

/// <summary>
/// Turns batches of consumed records into rows of <c>ldb.main.records</c> through the quack
/// <see cref="BufferedAppender"/>: extract, embed once per batch, append, flush. The caller
/// owns the lance-redirected connection and the transaction the flush rides — the flush and
/// the checkpoint commit or revert together there.
///
/// Not thread safe — one writer, the indexer's own loop.
/// </summary>
public sealed class KontextRecordsWriter : IDisposable {
    readonly RecordContentExtractor _extractContent;
    readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    readonly EmbeddingGenerationOptions _embeddingOptions;
    readonly ILogger _log;
    readonly BufferedAppender _appender;

    public KontextRecordsWriter(
        DuckDBAdvancedConnection connection,
        RecordContentExtractor extractContent,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        EmbeddingGenerationOptions embeddingOptions,
        ILogger<KontextRecordsWriter> log
    ) {
        _extractContent   = extractContent;
        _embeddings       = embeddings;
        _embeddingOptions = embeddingOptions;
        _log              = log;
        _appender         = new(connection, "records\0"u8);
    }

    /// <summary>Records the extractor refused or failed on — visible progress for the skip policy.</summary>
    public long SkippedRecords { get; private set; }

    /// <summary>
    /// Applies one batch: rows land for every record the extractor accepts, then one flush
    /// commits them. Returns the number of rows written.
    /// </summary>
    public async ValueTask<int> ProjectAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct) {
        var rows = new List<PendingRecord>(batch.Count);

        foreach (var record in batch) {
            // Control records ($checkpoint-received, $subscription-caughtUp) are consumer
            // plumbing riding the batch for their positions — never index them.
            if (record.SchemaInfo.SchemaName.StartsWith('$'))
                continue;

            string? content;
            try {
                content = _extractContent(record);
            } catch (Exception ex) {
                // A deterministic extractor failure never resolves by retrying — skip the
                // record and keep the index advancing. A stalled index is the worse outcome.
                SkippedRecords++;
                _log.LogWarning(ex, "Skipped record at {Position} ({SchemaName}): extractor failed", record.Position, record.SchemaInfo.SchemaName);
                continue;
            }

            if (content is null)
                continue;

            string stream = record.Position.StreamId;

            rows.Add(new(
                LogPosition: (long)record.Position.LogPosition.CommitPosition!.Value,
                RecordId: record.Id,
                Stream: stream,
                Category: GetStreamCategory(stream),
                SchemaName: record.SchemaInfo.SchemaName,
                SchemaId: record.Headers.TryGetValue(HeaderKeys.SchemaId, out var schemaId) ? schemaId : null,
                SchemaFormat: record.SchemaInfo.SchemaDataFormat.ToString(),
                Content: content,
                CreatedAt: new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds()));
        }

        if (rows.Count == 0)
            return 0;

        // One model call for the whole batch — per-record embedding is the dominant cost at
        // whole-log rates. A failure here propagates: nothing flushes, the transaction
        // reverts, and supervision replays the batch.
        var generated = await _embeddings
            .GenerateAsync(rows.Select(row => row.Content), _embeddingOptions, ct)
            .ConfigureAwait(false);

        foreach (var (pending, embedding) in rows.Zip(generated)) {
            var recordId = pending.RecordId;
            var row      = _appender.CreateRow();

            try {
                row.Add(pending.LogPosition);
                row.Add(MemoryMarshal.AsBytes(new ReadOnlySpan<Guid>(in recordId)));
                row.Add(pending.Stream);
                row.Add(pending.Category);
                row.Add(pending.SchemaName);
                row.Add(pending.SchemaId);
                row.Add(pending.SchemaFormat);
                row.Add(pending.Content);
                row.Add(pending.CreatedAt);
                row.Add(embedding.Vector.Span, CollectionType.Array);
            } finally {
                row.Dispose();
            }
        }

        _appender.Flush();

        return rows.Count;
    }

    public void Dispose() => _appender.Dispose();

    static string GetStreamCategory(string streamName) {
        var dashIndex = streamName.IndexOf('-');
        return dashIndex == -1 ? streamName : streamName[..dashIndex];
    }

    readonly record struct PendingRecord(
        long LogPosition,
        Guid RecordId,
        string Stream,
        string Category,
        string SchemaName,
        string? SchemaId,
        string SchemaFormat,
        string Content,
        long CreatedAt
    );
}
