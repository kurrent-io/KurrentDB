// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Memory.Data;
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Consumers.Configuration;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
/// </summary>
public sealed class KontextMemoryProjectorService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
    : SystemReadyBackgroundService(services, readyWhen, "KontextMemoryProjector") {
    // Changing the key orphans the stored checkpoint and replays the read model from the start.
    const string CheckpointKey = "KontextMemoryProjection";

    const int BatchSize = 500;

    static readonly TimeSpan BatchWindow = TimeSpan.FromSeconds(5);

    protected override async Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        var pool            = Services.GetRequiredService<KontextConnectionPool>();
        var embeddings      = Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var schemaOptions   = Services.GetRequiredService<KontextSchemaOptions>();
        var consumerBuilder = Services.GetRequiredService<IConsumerBuilder>();
        var loggerFactory   = Services.GetRequiredService<ILoggerFactory>();

        // The projector owns the write side end to end: the dedicated lance-redirected
        // connection (writers never rent), the checkpoint store — whose unqualified table lands
        // in the lance catalog via the redirection — and the per-batch transaction that carries
        // both the MERGE and the checkpoint. The writer only turns batches into statements here.
        await using var connection = pool.OpenLanceWriter();

        var checkpoints = new KontextCheckpointStore(CheckpointKey);
        checkpoints.EnsureSchema(connection);

        // The dimension is the schema's — the FLOAT[N] column type and the writer's cast must
        // agree, and both come from the same configured value.
        var writer = new KontextMemoryWriter(connection, embeddings, new EmbeddingGenerationOptions { Dimensions = schemaOptions.Dimension });

        // Only used when no checkpoint exists yet — resumption always wins. Earliest, unlike the
        // schema registry's Latest: the read model is rebuildable, so a fresh node projects the
        // full memory history before serving recalls.
        var startPosition = checkpoints.Load(connection);

        await using var consumer = consumerBuilder
            .ConsumerId(CheckpointKey)
            .Filter(KontextConventions.Filters.MemoriesFilter)
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .StartPosition(startPosition)
            .DisableAutoCommit()
            .DisableResiliencePipeline()
            .LoggerFactory(loggerFactory)
            .Create();

        await foreach (var batch in consumer.Records(stoppingToken).ReadBatches(BatchSize, BatchWindow, stoppingToken).ConfigureAwait(false)) {
            // Data and checkpoint in ONE transaction on the lance catalog — the only attached
            // database a lance-writing transaction may touch (the engine refuses a second).
            // Probed: the MERGE and the checkpoint land or revert together.
            using var tx = connection.BeginTransaction();

            await writer.ProjectAsync(batch, stoppingToken).ConfigureAwait(false);

            checkpoints.Store(connection, batch[^1].Position);

            tx.CommitOnDispose();
        }
    }
}

public static class KontextMemoryProjectorWireUpExtensions {
    extension(IServiceCollection services) {
        public IServiceCollection AddKontextMemoryProjector() {
            services.AddSystemReadiness();
            services.AddHostedService(sp => new KontextMemoryProjectorService(sp));
            return services;
        }
    }
}
