// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Surge.Consumers.Configuration;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Records.Indexer;

/// <summary>
/// Hosts <see cref="KontextRecordsIndexer"/> once the node reaches a serving state — hosting
/// only; the indexer owns the loop, its connection, and its supervision.
/// </summary>
public sealed class KontextRecordsIndexerService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
    : SystemReadyBackgroundService(services, readyWhen, "KontextRecordsIndexer") {
    protected override Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) =>
        new KontextRecordsIndexer(
            Services.GetRequiredService<KontextDataSource>(),
            Services.GetRequiredService<IConsumerBuilder>(),
            Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            Services.GetRequiredService<ILoggerFactory>()
        ).RunUntilStopped(stoppingToken);
}