// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Consumers.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Entities.Data;

/// <summary>
/// The entity catalog's read-model projector, the same loop as the memory projector but over the
/// catalog tables. After a batch that wrote aliases, folds the alias search indexes (throttled).
/// The FTS fold is correctness, over unfolded rows lance_fts returns the first k rows by scan
/// arrival, not the top k by score. The vector fold is latency only.
/// </summary>
public sealed class KontextEntityProjector(
    KontextDataSource dataSource,
    IConsumerBuilder consumerBuilder,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILoggerFactory loggerFactory
) {
    // Changing the key orphans the stored checkpoint and replays the read model from the start.
    const string CheckpointKey = "KontextEntityProjection";

    const int BatchSize = 500;

    static readonly TimeSpan BatchWindow              = TimeSpan.FromSeconds(5);
    static readonly TimeSpan IndexMaintenanceThrottle = TimeSpan.FromSeconds(30);
    static readonly TimeSpan InitialRestartDelay      = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaximumRestartDelay      = TimeSpan.FromSeconds(60);

    readonly ILogger _log = loggerFactory.CreateLogger<KontextEntityProjector>();

    public async Task RunUntilStopped(CancellationToken stoppingToken) {
        var restartDelay = InitialRestartDelay;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await ProjectUntilStopped(stoppingToken).ConfigureAwait(false);
                break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _log.LogError(ex, "Entity projector failed; restarting in {Delay}", restartDelay);

                try {
                    await Task.Delay(restartDelay, stoppingToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    break;
                }

                restartDelay = TimeSpan.FromTicks(Math.Min(restartDelay.Ticks * 2, MaximumRestartDelay.Ticks));
            }
        }
    }

    async Task ProjectUntilStopped(CancellationToken ct) {
        await using var connection = dataSource.OpenLanceWriter();

        var checkpoints = new KontextCheckpointStore(CheckpointKey);
        checkpoints.EnsureSchema(connection);

        var writer = new KontextEntityWriter(connection, embeddings, new EmbeddingGenerationOptions { Dimensions = KontextSchemaTask.Dimension });

        var startPosition = checkpoints.Load(connection);

        _log.LogInformation("Entity projector starting from {StartPosition}", startPosition);

        await using var consumer = consumerBuilder
            .ConsumerId(CheckpointKey)
            .Filter(KontextConventions.Filters.EntitiesFilter)
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .StartPosition(startPosition)
            .DisableAutoCommit()
            .DisableResiliencePipeline()
            .LoggerFactory(loggerFactory)
            .Create();

        var lastOptimize = TimeProvider.System.GetTimestamp();

        await foreach (var batch in consumer.Records(ct).ReadBatches(BatchSize, BatchWindow, ct).ConfigureAwait(false)) {
            var aliasesWritten = 0;

            using (var tx = connection.BeginTransaction()) {
                aliasesWritten = await writer.ProjectAsync(batch, ct).ConfigureAwait(false);

                checkpoints.Store(connection, batch[^1].Position);

                tx.CommitOnDispose();
            }

            if (aliasesWritten == 0 || TimeProvider.System.GetElapsedTime(lastOptimize) < IndexMaintenanceThrottle)
                continue;

            dataSource.EnsureInvertedIndex("entities");
            dataSource.EnsureVectorIndex("entities", "embedding");

            lastOptimize = TimeProvider.System.GetTimestamp();
        }
    }
}
