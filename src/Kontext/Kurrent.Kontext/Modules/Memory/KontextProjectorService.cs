// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Memory.Data;
using Kurrent.Surge;
using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.DuckDB.Projectors;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using static System.StringComparison;

namespace Kurrent.Kontext.Modules.Memory;

public sealed class KontextProjectorService<TProjection>(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
    : SystemReadyBackgroundService(services, readyWhen, GetServiceName()) where TProjection : KontextProjection {
  
    static string GetServiceName() {
        var name = typeof(TProjection).Name
            .Replace("Projection", "", OrdinalIgnoreCase)
            .Replace("Projector", "", OrdinalIgnoreCase);
        return $"{name}Projector";
    }

    protected override async Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        var projection         = Services.GetRequiredService<TProjection>();
        var consumerBuilder    = Services.GetRequiredService<IConsumerBuilder>();
        var connectionProvider = Services.GetRequiredService<IDuckDBConnectionProvider>();
        var loggerFactory      = Services.GetRequiredService<ILoggerFactory>();

        var options = new DuckDBProjectorOptions(connectionProvider) {
            Filter          = projection.Filter,
            InitialPosition = SubscriptionInitialPosition.Earliest, 
            AutoCommit      = new() {
                Enabled          = true,
                Interval         = TimeSpan.FromSeconds(5),
                RecordsThreshold = 500
            }
        };

        using var projector = new DuckDBProjector(options, projection, consumerBuilder, loggerFactory);
        
        await projector.RunUntilStopped(stoppingToken);
    }
}

public static class KontextProjectorWireUpExtensions {
    extension(IServiceCollection services) {
        public IServiceCollection AddKontextProjector<TProjection>() where TProjection : KontextProjection {
            services.AddSystemReadiness();
            services.TryAddSingleton<TProjection>();
            services.AddHostedService(sp => new KontextProjectorService<TProjection>(sp));
            return services;
        }
    }
}



// /// <summary>
// /// Runs the DuckDB projector for <typeparamref name="TProjection"/> once the node reaches a
// /// serving state. The gate waits once and never revokes: the read model is local to every node
// /// and rebuildable, so the projector runs on leaders, followers, and replicas alike, and a
// /// transient re-election must not interrupt it.
// /// </summary>
// public sealed class KontextProjectorService<TProjection>(IServiceProvider services) : SystemBackgroundService(
//     services.GetRequiredService<IPublisher>(),
//     services.GetRequiredService<ILogger<KontextProjectorService<TProjection>>>(),
//     GetServiceName<TProjection>()
// ) where TProjection : KontextProjection {
//     // Field initializer, not Run: the probe subscribes to the bus in its constructor, and it must
//     // be listening before the node's state transitions fire or the ready latch never opens.
//     readonly AdvancedSystemReadinessProbe _readinessProbe = new(
//         services.GetRequiredService<ISubscriber>(),
//         services.GetRequiredService<GetNodeSystemInfo>());
//
//
//     static string GetServiceName<T>() {
//         var name = typeof(TProjection).Name
//             .Replace("Projection", "", StringComparison.OrdinalIgnoreCase);
//         return $"{name}Projector";
//     }
//
//     protected override async Task RunAsync(CancellationToken stoppingToken) {
//         await _readinessProbe.WaitUntilReady(stoppingToken).ConfigureAwait(false);
//
//         var projection = services.GetRequiredService<TProjection>();
//
//         var options = new DuckDBProjectorOptions(services.GetRequiredService<IDuckDBConnectionProvider>()) {
//             Filter = projection.Filter,
//
//             // Earliest, unlike the schema registry's Latest: the read model is rebuildable, so
//             // a fresh node projects the full memory history before serving recalls. Only used
//             // when no checkpoint exists yet — resumption always wins.
//             InitialPosition = SubscriptionInitialPosition.Earliest,
//
//             // Enabled is explicit because it defaults to false — in which case the checkpoint
//             // only flushes on shutdown and every hard restart replays from the previous flush.
//             AutoCommit = new() {
//                 Enabled          = true,
//                 Interval         = TimeSpan.FromSeconds(5),
//                 RecordsThreshold = 500
//             }
//         };
//
//         var projector = new DuckDBProjector(
//             options, projection,
//             services.GetRequiredService<IConsumerBuilder>(),
//             services.GetRequiredService<ILoggerFactory>());
//
//         await projector.RunUntilStopped(stoppingToken).ConfigureAwait(false);
//     }
// }
//
// public static class KontextProjectorServiceRegistrationExtensions {
//     extension(IServiceCollection services) {
//         public IServiceCollection AddKontextProjector<TProjection>() where TProjection : KontextProjection {
//             services.AddNodeSystemInfoProvider();
//             services.TryAddSingleton<TProjection>();
//             services.AddHostedService(sp => new KontextProjectorService<TProjection>(sp));
//             return services;
//         }
//     }
// }
