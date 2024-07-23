using EventStore.Connect.Connectors;
using EventStore.Connect.Leases;
using EventStore.Connect.Schema;
using EventStore.Connectors.Control.Activation;
using EventStore.Connectors.Control.Coordination;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;

using static EventStore.Connectors.ConnectorsSystemConventions;

using ConnectContracts = EventStore.Streaming.Contracts;
using ControlContracts = EventStore.Connectors.Control.Contracts;

namespace EventStore.Connectors.Control;

public static class ControlPlaneWireUp {
    public static IServiceCollection AddConnectorsControlPlane(this IServiceCollection services) =>
        services
            .AddMessageSchemaRegistration()
            .AddConnectorsActivator()
            .AddConnectorsRegistry()
            .AddSingleton<INodeLifetimeService, NodeLifetimeService>()
            .AddHostedService<ControlPlaneCoordinatorService>();

    static IServiceCollection AddMessageSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask(
            "Connectors Control Schema Registration",
            static async (registry, ct) => {
                await RegisterControlSchema<ControlContracts.ActivatedConnectorsSnapshot>(registry, SchemaDefinitionType.Json, ct);
                await RegisterControlSchema<ConnectContracts.Processors.ProcessorStateChanged>(registry, SchemaDefinitionType.Json, ct);
                await RegisterControlSchema<ConnectContracts.Consumers.Checkpoint>(registry, SchemaDefinitionType.Json, ct);
                await RegisterControlSchema<Lease>(registry, SchemaDefinitionType.Json, ct); // must transform into a contract message...
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

    static IServiceCollection AddConnectorsRegistry(this IServiceCollection services) =>
        services
            .AddSingleton(new ConnectorsRegistryOptions {
                Filter           = Filters.ManagementFilter,
                SnapshotStreamId = Streams.ConnectorsRegistryStream
            })
            .AddSingleton<ConnectorsRegistry>()
            .AddSingleton<GetActiveConnectors>(static ctx => {
                var registry = ctx.GetRequiredService<ConnectorsRegistry>();
                return registry.GetConnectors;
            });

}