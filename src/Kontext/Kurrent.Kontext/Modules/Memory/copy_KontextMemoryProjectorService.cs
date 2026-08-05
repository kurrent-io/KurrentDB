// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using Kurrent.Kontext.Data;
// using Kurrent.Kontext.Infrastructure.Data;
// using Kurrent.Kontext.Modules.Memory.Data;
// using Kurrent.Surge;
// using Kurrent.Surge.Client;
// using Kurrent.Surge.Consumers.Configuration;
// using Kurrent.Surge.DuckDB;
// using KurrentDB.Core.Hosting;
// using KurrentDB.Core.Hosting.Experimental;
// using Microsoft.Extensions.AI;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
//
// namespace Kurrent.Kontext.Modules.Memory;
//
// /// <summary>
// /// The memories read-model projector: consumes the <c>$kontext/memories</c> stream directly
// /// through a Surge consumer, batches records with <c>ReadBatches</c> (count OR time window,
// /// whichever fills first), applies each batch through <see cref="KontextMemoryWriter"/>, and
// /// only then stores the checkpoint — the data always lands before the position that claims it.
// ///
// /// No processor, no projection module: three message types need a switch, not a router, and
// /// the checkpoint belongs in the same duck file as the data (a stream-side checkpoint shared
// /// across nodes would poison node-local read models).
// /// </summary>
// public sealed class KontextMemoryProjectorService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
//     : SystemReadyBackgroundService(services, readyWhen, "KontextMemoryProjector") {
//     // The DuckDBProjector wiring used the projection's name as its checkpoint key — kept
//     // verbatim so an existing read model resumes instead of replaying from the start.
//     const string CheckpointKey = "KontextMemoryProjection";
//
//     const int BatchSize = 500;
//
//     static readonly TimeSpan BatchWindow = TimeSpan.FromSeconds(5);
//
//     protected override async Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
//         var pool            = Services.GetRequiredService<KontextConnectionPool>();
//         var embeddings      = Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
//         var schemaOptions   = Services.GetRequiredService<KontextSchemaOptions>();
//         var consumerBuilder = Services.GetRequiredService<IConsumerBuilder>();
//         var loggerFactory   = Services.GetRequiredService<ILoggerFactory>();
//
//         // The dimension is the schema's — the FLOAT[N] column type and the writer's cast must
//         // agree, and both come from the same configured value.
//         using var writer = new KontextMemoryWriter(pool, embeddings, schemaOptions.Dimension);
//
//         // The checkpoint store gets its own connection: its table uses unqualified names, and on
//         // the writer's connection a future USE-based appender leg would drop it into the lance
//         // catalog instead of the native engine file.
//         using var checkpointConnection = pool.Open();
//
//         var checkpoints = new DuckDBCheckpointStore(
//             CheckpointKey, checkpointConnection, loggerFactory.CreateLogger<DuckDBCheckpointStore>());
//
//         await checkpoints.EnsureStoreExists(stoppingToken).ConfigureAwait(false);
//
//         // Only used when no checkpoint exists yet — resumption always wins. Earliest, unlike the
//         // schema registry's Latest: the read model is rebuildable, so a fresh node projects the
//         // full memory history before serving recalls.
//         var startPosition = await checkpoints.LoadCheckpoint(stoppingToken).ConfigureAwait(false);
//
//         await using var consumer = consumerBuilder
//             .ConsumerId(CheckpointKey)
//             .Filter(KontextConventions.Filters.MemoriesFilter)
//             .InitialPosition(SubscriptionInitialPosition.Earliest)
//             .StartPosition(startPosition)
//             .DisableAutoCommit()
//             .DisableResiliencePipeline()
//             .LoggerFactory(loggerFactory)
//             .Create();
//
//         await foreach (var batch in consumer.Records(stoppingToken).ReadBatches(BatchSize, BatchWindow, stoppingToken).ConfigureAwait(false)) {
//             await writer.ProjectAsync(batch, stoppingToken).ConfigureAwait(false);
//
//             await checkpoints.StoreCheckpoint(batch[^1].Position, stoppingToken).ConfigureAwait(false);
//         }
//     }
// }
//
// public static class KontextMemoryProjectorWireUpExtensions {
//     extension(IServiceCollection services) {
//         public IServiceCollection AddKontextMemoryProjector() {
//             services.AddSystemReadiness();
//             services.AddHostedService(sp => new KontextMemoryProjectorService(sp));
//             return services;
//         }
//     }
// }
