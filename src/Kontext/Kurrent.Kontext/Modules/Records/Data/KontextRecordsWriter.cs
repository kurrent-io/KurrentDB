// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Grpc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Records.Data;

/// <summary>
/// Turns batches of resolved events into rows of <c>ldb.main.records</c> through the raw quack
/// appender: extract, embed once per batch, append, flush. One flush is one atomic lance
/// commit, so the batch and the checkpoint it implies land together — a crash mid-batch
/// commits nothing and the batch replays cleanly from <c>max(log_position)</c>.
///
/// Owns the appender only. The caller owns the dedicated connection and must have redirected
/// it with <c>USE ldb</c> before construction: duckdb_appender_create has no catalog slot, so
/// session redirection is the only route into the lance catalog.
///
/// Not thread safe — one writer, the indexer's own loop.
/// </summary>
public sealed class KontextRecordsWriter : IDisposable {
    readonly ContentExtractor _extractContent;
    readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    readonly EmbeddingGenerationOptions _embeddingOptions;
    readonly ILogger _log;
    readonly Appender _appender;

    public KontextRecordsWriter(
        DuckDBAdvancedConnection connection,
        ContentExtractor extractContent,
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
    /// Applies one batch: rows land for every event the extractor accepts, then one flush
    /// commits them. Returns the number of rows written.
    /// </summary>
    public async ValueTask<int> ProjectAsync(IReadOnlyList<ResolvedEvent> batch, CancellationToken ct) {
        var rows = new List<PendingRecord>(batch.Count);

        foreach (var resolvedEvent in batch) {
            string? schemaFormat = null;
            string? schemaId     = null;

            if (resolvedEvent.Event.Properties.Length > 0) {
                var props = Struct.Parser.ParseFrom(resolvedEvent.Event.Properties.Span);
                schemaId = props.Fields.TryGetValue(Constants.RecordProperties.SchemaIdKey, out var schemaIdValue)
                    ? schemaIdValue.StringValue
                    : null;
                schemaFormat = props.Fields.TryGetValue(Constants.RecordProperties.SchemaFormatKey, out var dataFormatValue)
                    ? dataFormatValue.StringValue
                    : null;
            }

            schemaFormat ??= resolvedEvent.Event.IsJson ? "Json" : "Bytes";

            string? content;
            try {
                content = _extractContent(in resolvedEvent, schemaFormat);
            } catch (Exception ex) {
                // A deterministic extractor failure never resolves by retrying — skip the
                // record and keep the index advancing. A stalled index is the worse outcome.
                SkippedRecords++;
                _log.LogWarning(ex, "Skipped record at {LogPosition} ({SchemaName}): extractor failed", resolvedEvent.Event.LogPosition, resolvedEvent.Event.EventType);
                continue;
            }

            if (content is null)
                continue;

            rows.Add(new(
                LogPosition: resolvedEvent.Event.LogPosition,
                RecordId: resolvedEvent.Event.EventId,
                Stream: resolvedEvent.Event.EventStreamId,
                Category: GetStreamCategory(resolvedEvent.Event.EventStreamId),
                SchemaName: resolvedEvent.Event.EventType,
                SchemaId: schemaId,
                SchemaFormat: schemaFormat,
                Content: content,
                CreatedAt: new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds()));
        }

        if (rows.Count == 0)
            return 0;

        // One model call for the whole batch — per-event embedding is the dominant cost at
        // whole-log rates. A failure here propagates: the flush never runs, nothing commits,
        // and supervision replays the batch.
        var generated = await _embeddings
            .GenerateAsync(rows.Select(row => row.Content), _embeddingOptions, ct)
            .ConfigureAwait(false);

        foreach (var (pending, embedding) in rows.Zip(generated)) {
            var recordId = pending.RecordId;

            using var row = _appender.CreateRow();
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
