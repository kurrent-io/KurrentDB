// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Surge.Consumers.Configuration;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Entities.Data;

/// <summary>
/// Hosts the entity projector once the node reaches a serving state. Hosting only, the
/// projector owns the loop, its connection, and its supervision.
/// </summary>
public sealed class KontextEntityProjectorService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
    : SystemReadyBackgroundService(services, readyWhen, "KontextEntityProjector") {
    protected override Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) =>
        new KontextEntityProjector(
            Services.GetRequiredService<KontextDataSource>(),
            Services.GetRequiredService<IConsumerBuilder>(),
            Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            Services.GetRequiredService<ILoggerFactory>()
        ).RunUntilStopped(stoppingToken);
}

public static class KontextEntityProjectorWireUpExtensions {
    extension(IServiceCollection services) {
        public IServiceCollection AddKontextEntityProjector() {
            services.AddSystemReadiness();
            services.AddHostedService(sp => new KontextEntityProjectorService(sp));
            return services;
        }
    }
}
