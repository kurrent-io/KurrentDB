using EventStore.Connect.Connectors;
using EventStore.Connect.Leases;
using EventStore.Connect.Schema;
using EventStore.Connectors.Control.Coordination;
using EventStore.Connectors.System;
using EventStore.Streaming;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static EventStore.Connectors.ConnectorsFeatureConventions;

using ConnectContracts = EventStore.Streaming.Contracts;
using ControlContracts = EventStore.Connectors.Control.Contracts;

namespace EventStore.Connectors.Control;

public static class ControlPlaneWireUp {
    public static IServiceCollection AddConnectorsControlPlane(this IServiceCollection services) =>
        services
            .AddMessageSchemaRegistration()
            .AddConnectorsActivator()
            .AddConnectorsControlRegistry()
            .AddSingleton<INodeLifetimeService, NodeLifetimeService>()
            .AddSingleton<IHostedService, ConnectorsControlService>();

    static IServiceCollection AddMessageSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask(
            "Connectors Control Schema Registration",
            static async (registry, token) => {
                Task[] tasks = [
                    RegisterControlMessages<ControlContracts.ActivatedConnectorsSnapshot>(registry, token),
                    RegisterControlMessages<ConnectContracts.Processors.ProcessorStateChanged>(registry, token),
                    RegisterControlMessages<ConnectContracts.Consumers.Checkpoint>(registry, token),
                    RegisterControlMessages<Lease>(registry, token), //TODO SS: transform Lease into a message contract in Connect
                ];

                await tasks.WhenAll();
            }
        );

    static IServiceCollection AddConnectorsActivator(this IServiceCollection services) =>
        services
            .AddSingleton(new SystemConnectorsFactoryOptions {
                LifecycleStreamTemplate   = Streams.LifecycleStreamTemplate,
                CheckpointsStreamTemplate = Streams.CheckpointsStreamTemplate,
                LeasesStreamTemplate      = Streams.LeasesStreamTemplate
            })
            .AddSingleton<ConnectorsActivator>();

    static IServiceCollection AddConnectorsControlRegistry(this IServiceCollection services) =>
        services
            .AddSingleton(new ConnectorsControlRegistryOptions {
                Filter           = Filters.ManagementFilter,
                SnapshotStreamId = Streams.ConnectorsRegistryStream
            })
            .AddSingleton<ConnectorsControlRegistry>()
            .AddSingleton<GetActiveConnectors>(static ctx => {
                var registry = ctx.GetRequiredService<ConnectorsControlRegistry>();
                return registry.GetConnectors;
            });

}