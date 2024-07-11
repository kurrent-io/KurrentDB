using System.Text.Json;
using EventStore.Connectors.Control.Coordination;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors.Control;

public static class ControlPlaneWireUp {
    public static void AddConnectorsControlPlane(this IServiceCollection services, SchemaRegistry schemaRegistry) {
        // // Because (unfortunately) the gossip listener service raises a schemaless event,
        // // a proper schema must be registered for it to be consumed by the control plane
        // schemaRegistry.RegisterSchema<GossipUpdatedInMemory>(GossipListenerService.EventType, SchemaDefinitionType.Json)
        //     .AsTask().GetAwaiter().GetResult();

        // schemaRegistry.RegisterSchema<Streaming.Contracts.Processors.ProcessorStateChanged>(
        //     GetLifetimeSystemEventName<Streaming.Contracts.Processors.ProcessorStateChanged>(),
        //     SchemaDefinitionType.Protobuf
        // ).AsTask().GetAwaiter().GetResult();

        // schemaRegistry.RegisterSchema<Streaming.Contracts.Consumers.Checkpoint>(
        //     GetLifetimeSystemEventName<Streaming.Contracts.Consumers.Checkpoint>(),
        //     SchemaDefinitionType.Protobuf
        // ).AsTask().GetAwaiter().GetResult();

        // services.AddSingleton<INodeLifetimeService, NodeLifetimeService>(sp => new NodeLifetimeService(
        //     sp.GetRequiredService<ISubscriber>(),
        //     sp.GetRequiredService<ILogger<NodeLifetimeService>>()
        // ));

        services.AddSingleton<INodeLifetimeService, NodeLifetimeService>();

        services.AddSingleton<GetNodeInstanceInfo>(ctx => {
            // TODO SS: Suggest that the node info is added to the services collection
            var publisher = ctx.GetRequiredService<IPublisher>();
            var time      = ctx.GetRequiredService<TimeProvider>();

            return async () => {
                var resolvedEvent = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream);
                var gossipUpdated = JsonSerializer.Deserialize<GossipUpdatedInMemory>(resolvedEvent!.Value.Event.Data.Span)!;

                var nodeInfo = new NodeInstanceInfo(
                    gossipUpdated.Members.Single(x => x.InstanceId == gossipUpdated.NodeId),
                    time.GetUtcNow()
                );

                return nodeInfo;
            };
        });

        services.AddHostedService<ControlPlaneCoordinatorService>();
    }
}