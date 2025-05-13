// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Connect.Connectors;
using KurrentDB.Connect.Schema;
using Humanizer;
using Kurrent.Surge.Leases;
using Kurrent.Surge;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using KurrentDB.Connectors.Infrastructure.System.Node;
using KurrentDB.Connectors.Planes.Management;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static KurrentDB.Connectors.Planes.ConnectorsFeatureConventions;

using SurgeContracts = Kurrent.Surge.Protocol;
using ControlContracts = KurrentDB.Connectors.Control.Contracts;

namespace KurrentDB.Connectors.Planes.Control;

public static class ControlPlaneWireUp {
    public static IServiceCollection AddConnectorsControlPlane(this IServiceCollection services) {
        services
            .AddMessageSchemaRegistration()
            .AddConnectorsActivator()
            .AddConnectorsControlRegistry()
            .AddSingleton<GetNodeLifetimeService>(ctx =>
                component => new NodeLifetimeService(
                    component,
                    ctx.GetRequiredService<IPublisher>(),
                    ctx.GetRequiredService<ISubscriber>(),
                    ctx.GetService<ILogger<NodeLifetimeService>>()));

        services.AddSingleton<IHostedService, ConnectorsControlService>();

        return services;
    }

    static IServiceCollection AddMessageSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask(
            "Connectors Control Schema Registration",
            static async (registry, token) => {
                Task[] tasks = [
                    RegisterControlMessages<ControlContracts.ActivatedConnectorsSnapshot>(registry, token),
                    RegisterControlMessages<SurgeContracts.Processors.ProcessorStateChanged>(registry, token),
                    RegisterControlMessages<SurgeContracts.Consumers.Checkpoint>(registry, token),
                    RegisterControlMessages<Lease>(registry, token)
                ];

                await tasks.WhenAll();
            }
        );

    static IServiceCollection AddConnectorsActivator(this IServiceCollection services) =>
        services
            .AddSingleton<ISystemConnectorFactory>(ctx => {
                var commandApplication = ctx.GetRequiredService<ConnectorsCommandApplication>();

                var options = new SystemConnectorsFactoryOptions {
                    CheckpointsStreamTemplate = Streams.CheckpointsStreamTemplate,
                    AutoLock = new() {
                        LeaseDuration      = 5.Seconds(),
                        AcquisitionTimeout = 60.Seconds(),
                        AcquisitionDelay   = 5.Seconds(),
                        StreamTemplate     = Streams.LeasesStreamTemplate
                    },
                    Interceptors = new([new ConnectorsLifecycleInterceptor(commandApplication)])
                };

                return new SystemConnectorsFactory(options, ctx);
            })
            .AddSingleton<ConnectorsActivator>();

    static IServiceCollection AddConnectorsControlRegistry(this IServiceCollection services) =>
        services
            .AddSingleton(new ConnectorsControlRegistryOptions {
                Filter           = Filters.ManagementFilter,
                SnapshotStreamId = Streams.ControlConnectorsRegistryStream
            })
            .AddSingleton<ConnectorsControlRegistry>()
            .AddSingleton<GetActiveConnectors>(static ctx => {
                var registry = ctx.GetRequiredService<ConnectorsControlRegistry>();
                return registry.GetConnectors;
            });
}
