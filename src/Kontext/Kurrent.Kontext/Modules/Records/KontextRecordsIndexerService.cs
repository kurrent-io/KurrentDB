// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Records.Data;
using Kurrent.Surge.Client;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Records;

/// <summary>
/// The whole-log records indexer: subscribes to <c>$all</c> from the last committed position,
/// extracts searchable text through the <see cref="ContentExtractor"/>, embeds each batch, and
/// appends into <c>ldb.main.records</c> — one flush per batch, one lance commit per flush.
///
/// Runs on every node, leader and follower alike (<c>requiresLeader: false</c>): the log is
/// the replicated thing, the index is a local derivation into node-local storage — the same
/// rule the secondary indexes follow.
///
/// Supervision: a dead subscription restarts with exponential backoff from the checkpoint the
/// table itself carries. The reference pipeline dies silently on a poison event; this one does
/// not, by design.
/// </summary>
public sealed class KontextRecordsIndexerService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
    : SystemReadyBackgroundService(services, readyWhen, "KontextRecordsIndexer") {
    const int BatchSize = 500;

    static readonly TimeSpan BatchWindow          = TimeSpan.FromSeconds(5);
    static readonly TimeSpan VectorIndexThrottle  = TimeSpan.FromSeconds(30);
    static readonly TimeSpan InitialRestartDelay  = TimeSpan.FromSeconds(5);
    static readonly TimeSpan MaximumRestartDelay  = TimeSpan.FromSeconds(60);

    // The catch-up channel is the only backpressure lever, and it holds full read responses —
    // a modest bound, not the secondary indexes' 100k standing reservation.
    const int CatchUpBufferSize = BatchSize * 2;

    protected override async Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        var pool           = Services.GetRequiredService<KontextConnectionPool>();
        var publisher      = Services.GetRequiredService<IPublisher>();
        var embeddings     = Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var schemaOptions  = Services.GetRequiredService<KontextSchemaOptions>();
        var extractContent = Services.GetRequiredService<ContentExtractor>();
        var log            = Services.GetRequiredService<ILoggerFactory>().CreateLogger<KontextRecordsIndexerService>();
        var writerLog      = Services.GetRequiredService<ILoggerFactory>().CreateLogger<KontextRecordsWriter>();

        var schema = new KontextRecordsSchema(pool, schemaOptions);
        await schema.CreateAsync(stoppingToken).ConfigureAwait(false);

        var restartDelay = InitialRestartDelay;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await IndexUntilStopped(schema, pool, publisher, embeddings, schemaOptions, extractContent, log, writerLog, stoppingToken).ConfigureAwait(false);
                break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                log.LogError(ex, "Records indexer failed; restarting in {Delay}", restartDelay);

                try {
                    await Task.Delay(restartDelay, stoppingToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    break;
                }

                restartDelay = TimeSpan.FromTicks(Math.Min(restartDelay.Ticks * 2, MaximumRestartDelay.Ticks));
            }
        }
    }

    async Task IndexUntilStopped(
        KontextRecordsSchema schema,
        KontextConnectionPool pool,
        IPublisher publisher,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        KontextSchemaOptions schemaOptions,
        ContentExtractor extractContent,
        ILogger log,
        ILogger<KontextRecordsWriter> writerLog,
        CancellationToken ct
    ) {
        // The writer's dedicated connection, redirected once: duckdb_appender_create has no
        // catalog slot, so USE is the only route into the lance catalog. The checkpoint reads
        // on this same fresh connection — a rented one can hold a stale dataset handle.
        await using var connection = pool.Open();
        using (var command = connection.CreateCommand()) {
            command.CommandText = "USE ldb";
            command.ExecuteNonQuery();
        }

        var lastPosition = schema.ReadLastPosition(connection);
        var startFrom    = lastPosition is { } position ? Position.FromInt64(position, position) : Position.Start;

        log.LogInformation("Records indexer starting from {StartFrom}", startFrom);

        using var writer = new KontextRecordsWriter(
            connection, extractContent, embeddings,
            new EmbeddingGenerationOptions { Dimensions = schemaOptions.Dimension }, writerLog);

        await using var subscription = new Enumerator.AllSubscription(
            bus: publisher,
            expiryStrategy: DefaultExpiryStrategy.Instance,
            checkpoint: startFrom,
            resolveLinks: false,
            user: SystemAccounts.System,
            requiresLeader: false,
            catchUpBufferSize: CatchUpBufferSize,
            cancellationToken: ct);

        var lastOptimize = TimeProvider.System.GetTimestamp();

        await foreach (var batch in ReadEvents(subscription, log, ct).ReadBatches(BatchSize, BatchWindow, ct).ConfigureAwait(false)) {
            var written = await writer.ProjectAsync(batch, ct).ConfigureAwait(false);

            log.LogDebug("Records indexer committed {Written} of {BatchSize} records", written, batch.Count);

            if (written == 0 || TimeProvider.System.GetElapsedTime(lastOptimize) < VectorIndexThrottle)
                continue;

            await schema.EnsureVectorIndexAsync(ct).ConfigureAwait(false);
            lastOptimize = TimeProvider.System.GetTimestamp();
        }
    }

    static async IAsyncEnumerable<ResolvedEvent> ReadEvents(
        Enumerator.AllSubscription subscription,
        ILogger log,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        while (!ct.IsCancellationRequested && await subscription.MoveNextAsync().ConfigureAwait(false)) {
            switch (subscription.Current) {
                case ReadResponse.SubscriptionCaughtUp caughtUp:
                    log.LogInformation("Records indexer caught up at {Time}", caughtUp.Timestamp);
                    continue;

                case ReadResponse.EventReceived received:
                    // System events never index. The filter lives here, beside the subscription,
                    // because the writer must stay callable with pre-filtered batches only.
                    if (received.Event.Event.EventType.StartsWith('$') || received.Event.Event.EventStreamId.StartsWith('$'))
                        continue;

                    yield return received.Event;
                    continue;

                default:
                    continue;
            }
        }
    }
}

public static class KontextRecordsIndexerWireUpExtensions {
    extension(IServiceCollection services) {
        public IServiceCollection AddKontextRecordsIndexer(ContentExtractor? extractContent = null) {
            services.AddSystemReadiness();
            services.TryAddSingleton<ContentExtractor>(extractContent ?? KontextRecordsContent.Json);
            services.AddHostedService(sp => new KontextRecordsIndexerService(sp));
            return services;
        }
    }
}
