// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Records.Data;
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Consumers.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Records;

/// <summary>
/// The whole-log records indexer: consumes <c>$all</c> through a Surge consumer (system events
/// filtered server-side), extracts searchable text through the <see cref="RecordContentExtractor"/>,
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
    RecordContentExtractor extractContent,
    ILoggerFactory loggerFactory
) {
    // Changing the key orphans the stored checkpoint and replays the index from the start.
    const string CheckpointKey = "KontextRecordsIndexer";

    const int BatchSize = 500;

    static readonly TimeSpan BatchWindow         = TimeSpan.FromSeconds(5);
    static readonly TimeSpan IndexMaintenanceThrottle = TimeSpan.FromSeconds(30);
    static readonly TimeSpan InitialRestartDelay = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaximumRestartDelay = TimeSpan.FromSeconds(60);

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
            connection, extractContent, embeddings,
            new EmbeddingGenerationOptions { Dimensions = KontextSchemaTask.Dimension },
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

        await foreach (var batch in consumer.Records(ct).ReadBatches(BatchSize, BatchWindow, ct).ConfigureAwait(false)) {
            var written = 0;

            // Data and checkpoint in ONE transaction on the single catalog a lance-writing
            // transaction may touch — probed: the appender flush and the checkpoint MERGE
            // commit or revert together. batch[^1] may be a control record, whose position
            // still advances the checkpoint through skipped stretches.
            using (var tx = connection.BeginTransaction()) {
                written = await writer.ProjectAsync(batch, ct).ConfigureAwait(false);

                checkpoints.Store(connection, batch[^1].Position);

                tx.CommitOnDispose();
            }

            _log.LogDebug("Records indexer committed {Written} of {BatchSize} records", written, batch.Count);

            if (written == 0 || TimeProvider.System.GetElapsedTime(lastOptimize) < IndexMaintenanceThrottle)
                continue;

            // FTS first — over unfolded rows lance_fts returns the first k rows by scan
            // arrival, not the top k by score, so the fold is correctness; the vector fold
            // is only latency.
            dataSource.EnsureInvertedIndex("records");
            dataSource.EnsureVectorIndex("records", "embedding");

            lastOptimize = TimeProvider.System.GetTimestamp();
        }
    }
}
