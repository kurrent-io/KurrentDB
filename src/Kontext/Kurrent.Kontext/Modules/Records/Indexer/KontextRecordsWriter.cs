// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Kurrent.Kontext.Embeddings.Normalization;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using Kurrent.Surge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Records.Data;

/// <summary>
/// Turns batches of consumed records into rows of <c>ldb.main.records</c> through the quack
/// <see cref="BufferedAppender"/>: flatten, embed once per batch, append, flush. The caller
/// owns the lance-redirected connection and the transaction the flush rides — the flush and
/// the checkpoint commit or revert together there.
///
/// Not thread safe — one writer, the indexer's own loop.
/// </summary>
public sealed class KontextRecordsWriter : IDisposable {
    readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    readonly EmbeddingGenerationOptions _embeddingOptions;
    readonly ILogger _log;
    readonly BufferedAppender _appender;

    public KontextRecordsWriter(
        DuckDBAdvancedConnection connection,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        EmbeddingGenerationOptions embeddingOptions,
        ILogger<KontextRecordsWriter> log
    ) {
        _embeddings       = embeddings;
        _embeddingOptions = embeddingOptions;
        _log              = log;
        _appender         = new(connection, "records\0"u8);
    }

    /// <summary>Records the table refused — an empty payload. Visible progress for the skip policy.</summary>
    public long SkippedRecords { get; private set; }

    /// <summary>
    /// Applies one batch: a row lands for every JSON record carrying a payload, then one flush
    /// commits them. Returns the number of rows written.
    /// </summary>
    public async ValueTask<int> ProjectAsync(IReadOnlyList<SurgeRecord> batch, CancellationToken ct) {
        var pendingRecords = new List<PendingRecord>(batch.Count);

        foreach (var record in batch) {
            if (JsonNormalizer.Instance.Normalize(record.Data.Span) is not { } content) {
                SkippedRecords++;
                _log.LogTrace("Skipped record at {Position} ({SchemaName}): empty payload", record.Position, record.SchemaInfo.SchemaName);
                continue;
            }

            var data = Encoding.UTF8.GetString(record.Data.Span);

            var stream = record.Position.StreamId;

            var pendingRecord = new PendingRecord(
                LogPosition: (long)record.Position.LogPosition.CommitPosition!.Value,
                RecordId: record.Id,
                Stream: stream,
                Category: GetStreamCategory(stream),
                SchemaName: record.SchemaInfo.SchemaName,
                SchemaFormat: record.SchemaInfo.SchemaDataFormat.ToString(),
                SchemaId: record.Headers.TryGetValue(HeaderKeys.SchemaId, out var schemaId) ? schemaId : null,
                Data: data,
                CreatedAt: new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds(),
                Content: content);

            pendingRecords.Add(pendingRecord);
        }

        if (pendingRecords.Count == 0)
            return 0;

        // One model call for the whole batch — per-record embedding is the dominant cost at
        // whole-log rates. A failure here propagates: nothing flushes, the transaction
        // reverts, and supervision replays the batch.
        var generated = await _embeddings
            .GenerateAsync(pendingRecords.Select(row => row.Content), _embeddingOptions, ct)
            .ConfigureAwait(false);

        foreach (var (pending, embedding) in pendingRecords.Zip(generated)) {
            var recordId = pending.RecordId;
            var row      = _appender.CreateRow();

            try {
                row.Add(pending.LogPosition);
                row.Add(recordId.AsBytes());
                row.Add(pending.Stream);
                row.Add(pending.Category);
                row.Add(pending.SchemaName);
                row.Add(pending.SchemaFormat);
                row.Add(pending.SchemaId);
                row.Add(pending.Data);
                row.Add(pending.CreatedAt);
                row.Add(pending.Content);
                row.Add(embedding.Vector.Span, CollectionType.Array);
            } finally {
                row.Dispose();
            }
        }

        _appender.Flush();

        return pendingRecords.Count;
        
        static string GetStreamCategory(string streamName) {
            var dashIndex = streamName.IndexOf('-');
            return dashIndex == -1 ? streamName : streamName[..dashIndex];
        }
    }

    public void Dispose() => _appender.Dispose();

    readonly record struct PendingRecord(
        long LogPosition,
        Guid RecordId,
        string Stream,
        string Category,
        string SchemaName,
        string SchemaFormat,
        string? SchemaId,
        string Data,
        long CreatedAt,
        string Content
    );
}

static class GuidExtensions {
    /// <summary>
    /// Returns a ReadOnlySpan pointing directly to the Guid's memory. 
    /// Zero allocation and zero copying.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> AsBytes(ref readonly this Guid guid) =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Guid, byte>(ref Unsafe.AsRef(in guid)), 16);
}
