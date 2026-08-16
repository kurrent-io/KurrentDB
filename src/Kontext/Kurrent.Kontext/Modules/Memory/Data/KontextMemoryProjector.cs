// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Memory.Data;
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Consumers.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Memory;

/// <summary>
/// The memories read-model projector: consumes the <c>$kontext/memories</c> stream directly
/// through a Surge consumer, batches records with <c>ReadBatches</c> (count OR time window,
/// whichever fills first), applies each batch through <see cref="KontextMemoryWriter"/>, and
/// only then stores the checkpoint — the data always lands before the position that claims it.
///
/// No processor, no projection module: three message types need a switch, not a router, and
/// the checkpoint belongs in the same LANCE catalog as the data — a stream-side checkpoint
/// shared across nodes would poison node-local read models, and a transaction that writes
/// lance cannot touch any other attached database, so an engine-catalog checkpoint could
/// never share the batch transaction (probed, TransactionLanceProbeTests).
///
/// Supervision restarts a dead loop with exponential backoff: writers carry the lance
/// commit-conflict handling, and a restart re-opens the connection and resumes from the
/// checkpoint — the batch transaction makes the replay exact.
/// </summary>
public sealed class KontextMemoryProjector(
    KontextDataSource dataSource,
    IConsumerBuilder consumerBuilder,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILoggerFactory loggerFactory
) {
    // Changing the key orphans the stored checkpoint and replays the read model from the start.
    const string CheckpointKey = "KontextMemoryProjection";

    const int BatchSize = 500;

    static readonly TimeSpan BatchWindow         = TimeSpan.FromSeconds(5);
    static readonly TimeSpan InitialRestartDelay = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaximumRestartDelay = TimeSpan.FromSeconds(60);

    readonly ILogger _log = loggerFactory.CreateLogger<KontextMemoryProjector>();

    public async Task RunUntilStopped(CancellationToken stoppingToken) {
        var restartDelay = InitialRestartDelay;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await ProjectUntilStopped(stoppingToken).ConfigureAwait(false);
                break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _log.LogError(ex, "Memory projector failed; restarting in {Delay}", restartDelay);

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
        // The projector owns the write side end to end: the dedicated lance-redirected
        // connection (writers never rent), the checkpoint store — whose unqualified table lands
        // in the lance catalog via the redirection — and the per-batch transaction that carries
        // both the MERGE and the checkpoint. The writer only turns batches into statements here.
        await using var connection = dataSource.OpenLanceWriter();

        var checkpoints = new KontextCheckpointStore(CheckpointKey);
        checkpoints.EnsureSchema(connection);

        // The dimension is the schema's — the FLOAT[N] column type and the writer's cast must
        // agree, and both come from KontextSchemaTask.Dimension.
        var writer = new KontextMemoryWriter(connection, embeddings, new EmbeddingGenerationOptions { Dimensions = KontextSchemaTask.Dimension });

        // Only used when no checkpoint exists yet — resumption always wins. Earliest, unlike the
        // schema registry's Latest: the read model is rebuildable, so a fresh node projects the
        // full memory history before serving recalls.
        var startPosition = checkpoints.Load(connection);

        _log.LogInformation("Memory projector starting from {StartPosition}", startPosition);

        await using var consumer = consumerBuilder
            .ConsumerId(CheckpointKey)
            .Filter(KontextConventions.Filters.MemoriesFilter)
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .StartPosition(startPosition)
            .DisableAutoCommit()
            .DisableResiliencePipeline()
            .LoggerFactory(loggerFactory)
            .Create();

        await foreach (var batch in consumer.Records(ct).ReadBatches(BatchSize, BatchWindow, ct).ConfigureAwait(false)) {
            // Data and checkpoint in ONE transaction on the lance catalog — the only attached
            // database a lance-writing transaction may touch (the engine refuses a second).
            // Probed: the MERGE and the checkpoint land or revert together.
            using var tx = connection.BeginTransaction();

            await writer.ProjectAsync(batch, ct).ConfigureAwait(false);

            checkpoints.Store(connection, batch[^1].Position);

            tx.CommitOnDispose();
        }
    }
}
