// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Surge.Consumers.Configuration;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Memory;

/// <summary>
/// Hosts <see cref="KontextMemoryProjector"/> once the node reaches a serving state — hosting
/// only; the projector owns the loop, its connection, and its supervision.
/// </summary>
public sealed class KontextMemoryProjectorService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
    : SystemReadyBackgroundService(services, readyWhen, "KontextMemoryProjector") {
    protected override Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) =>
        new KontextMemoryProjector(
            Services.GetRequiredService<KontextConnectionPool>(),
            Services.GetRequiredService<IConsumerBuilder>(),
            Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            Services.GetRequiredService<KontextSchemaOptions>(),
            Services.GetRequiredService<ILoggerFactory>()
        ).RunUntilStopped(stoppingToken);
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
