using EventStore.Connect.Connectors;
using EventStore.Connect.Leases;
using EventStore.Connect.Schema;
using EventStore.Connectors.Control.Coordination;
using EventStore.Connectors.System;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;

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
            .AddHostedService<ConnectorsControlService>();

    // public static ISchemaRegistry RegisterConnectorsControlMessages(this ISchemaRegistry registry) {
    //     RegisterControlSchema<ControlContracts.ActivatedConnectorsSnapshot>(registry, SchemaDefinitionType.Json).GetAwaiter().GetResult();
    //     RegisterControlSchema<ConnectContracts.Processors.ProcessorStateChanged>(registry, SchemaDefinitionType.Json).GetAwaiter().GetResult();
    //     RegisterControlSchema<ConnectContracts.Consumers.Checkpoint>(registry, SchemaDefinitionType.Json).GetAwaiter().GetResult();
    //     RegisterControlSchema<Lease>(registry, SchemaDefinitionType.Json).GetAwaiter().GetResult(); //TODO SS: transform Lease into a message contract in Connect
    //
    //     return registry;
    // }

    static IServiceCollection AddMessageSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask(
            "Connectors Control Schema Registration",
            static async (registry, ct) => {
                await RegisterControlSchema<ControlContracts.ActivatedConnectorsSnapshot>(registry, SchemaDefinitionType.Json, ct);
                await RegisterControlSchema<ConnectContracts.Processors.ProcessorStateChanged>(registry, SchemaDefinitionType.Json, ct);
                await RegisterControlSchema<ConnectContracts.Consumers.Checkpoint>(registry, SchemaDefinitionType.Json, ct);
                await RegisterControlSchema<Lease>(registry, SchemaDefinitionType.Json, ct); //TODO SS: transform Lease into a message contract in Connect
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