// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data.LanceDB;
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.Schema;
using KurrentDB.Core.Services.Transport.Enumerators;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Records.Indexer;

/// <summary>
/// The whole-log records indexer: consumes <c>$all</c> through a Surge consumer (system events
/// filtered server-side), flattens each payload through the <see cref="KontextRecordsWriter"/>,
/// embeds each batch, and appends into <c>ldb.main.records</c>. Each batch and its checkpoint
/// share ONE transaction on the lance-redirected writer connection — they commit or revert
/// together, so resume is a plain checkpoint load.
///
/// Runs on every node: the log is the replicated thing, the index is a local derivation into
/// node-local storage. Supervision restarts a dead loop with exponential backoff — a poison
/// record is skipped by the writer, never fatal.
/// </summary>
public sealed class KontextRecordsIndexer(
    KontextDataSource dataSource,
    IConsumerBuilder consumerBuilder,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILoggerFactory loggerFactory
) {
    // Changing the key orphans the stored checkpoint and replays the index from the start.
    const string CheckpointKey = "KontextRecordsIndexer";

    const int BatchSize = 500;

    const string RecordsTable = "ldb.main.records";

    static readonly TimeSpan BatchWindow              = TimeSpan.FromSeconds(5);
    static readonly TimeSpan IndexMaintenanceThrottle = TimeSpan.FromSeconds(30);
    static readonly TimeSpan InitialRestartDelay      = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaximumRestartDelay      = TimeSpan.FromSeconds(60);

    readonly ILogger _log = loggerFactory.CreateLogger<KontextRecordsIndexer>();

    public async Task RunUntilStopped(CancellationToken stoppingToken) {
        var restartDelay = InitialRestartDelay;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await IndexUntilStopped(stoppingToken).ConfigureAwait(false);
                break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _log.LogError(ex, "Records indexer failed; restarting in {Delay}", restartDelay);

                try {
                    await Task.Delay(restartDelay, stoppingToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    break;
                }

                restartDelay = TimeSpan.FromTicks(Math.Min(restartDelay.Ticks * 2, MaximumRestartDelay.Ticks));
            }
        }
    }

    async Task IndexUntilStopped(CancellationToken ct) {
        await using var connection = dataSource.OpenLanceWriter();

        var checkpoints = new KontextCheckpointStore(CheckpointKey);
        checkpoints.EnsureSchema(connection);

        var startPosition = checkpoints.Load(connection);

        _log.LogInformation("Records indexer starting from {StartPosition}", startPosition);

        using var writer = new KontextRecordsWriter(
            connection, embeddings,
            new() { Dimensions = KontextIndexConstants.VectorsDimension },
            loggerFactory.CreateLogger<KontextRecordsWriter>());

        await using var consumer = consumerBuilder
            .ConsumerId(CheckpointKey)
            .Filter(KontextConventions.Filters.RecordsIndexFilter)
            .SkipDecoding()
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .StartPosition(startPosition)
            .DisableAutoCommit()
            .DisableResiliencePipeline()
            .LoggerFactory(loggerFactory)
            .Create();

        var lastOptimize = TimeProvider.System.GetTimestamp();

        var batches = consumer.Records(ct)
            .Where(static record => record.Value is ReadResponse 
                                 || record.SchemaInfo.SchemaDataFormat == SchemaDataFormat.Json)
            .ReadBatched(
                BatchSize, BatchWindow,
                classify: static record => record.Value is ReadResponse
                    ? BatchAction<SurgeRecord, IndexBatch>.FlushThenYield(new([], record.Position))
                    : BatchAction<SurgeRecord, IndexBatch>.Batch(record),
                batchToOutput: static records => new(records, records[^1].Position),
                ct);

        await foreach (var batch in batches.ConfigureAwait(false)) {
            if (batch.Records.Count == 0) {
                checkpoints.Store(connection, batch.Position);
                continue;
            }

            int written;

            using (var tx = connection.BeginTransaction()) {
                written = await writer.ProjectAsync(batch.Records, ct).ConfigureAwait(false);
                checkpoints.Store(connection, batch.Position);
                tx.CommitOnDispose();
            }

            _log.LogDebug("Records indexer committed {Written} of {BatchSize} records", written, batch.Records.Count);

            if (written == 0 || TimeProvider.System.GetElapsedTime(lastOptimize) < IndexMaintenanceThrottle)
                continue;

            // FTS first — over unfolded rows lance_fts returns the first k rows by scan
            // arrival, not the top k by score, so the fold is correctness; the vector fold
            // is only latency.
            connection.EnsureInvertedIndex(RecordsTable, "data");
            connection.EnsureInvertedIndex(RecordsTable, "content");

            connection.EnsureVectorIndex(RecordsTable, "embedding", new LanceIvfPqIndexOptions {
                NumSubVectors = KontextIndexConstants.VectorsDimension / 8,
                NumPartitions = LancePartitions.For(connection.GetTableInfo(RecordsTable)?.RowCount ?? 0),
            });

            lastOptimize = TimeProvider.System.GetTimestamp();
        }
    }

    readonly record struct IndexBatch(IReadOnlyList<SurgeRecord> Records, RecordPosition Position);
}
