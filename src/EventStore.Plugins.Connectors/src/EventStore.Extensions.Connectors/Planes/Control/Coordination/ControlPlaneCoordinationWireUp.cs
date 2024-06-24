using System.Text.Json;
using EventStore.Connectors.Control.Assignment;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Hosting;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Control.Coordination;

public static class ControlPlaneCoordinationWireUp {
    public static void AddConnectorsCoordination(this IServiceCollection services) {
        // services.AddSingleton<ConnectorsManagementClient>(
        //     ctx => new ConnectorsManagementClient(ctx.GetRequiredService<SystemReader>())
        // );
        //
        // services.AddSingleton<ConnectorAssignorProvider>();

        // services.AddHostedService(ctx => new ProcessorWorker<SystemProcessor>(CreateProcessor(ctx), ctx));
    }

    // public static SystemProcessor CreateProcessor(IServiceProvider serviceProvider) {
    //     var publisher        = serviceProvider.GetRequiredService<IPublisher>();
    //     var loggerFactory    = serviceProvider.GetRequiredService<ILoggerFactory>();
    //     var assignorProvider = serviceProvider.GetRequiredService<ConnectorAssignorProvider>();
    //     var managementClient = serviceProvider.GetRequiredService<ConnectorsManagementClient>();
    //
    //     var coordinator = new ConnectorsCoordinator(assignorProvider.Get, managementClient.GetConnectors);
    //
    //     string[] streams = [
    //         // reacts to cluster topology changes
    //         SystemStreams.GossipStream,
    //         // // reacts to task manager events: started, stopped, etc.
    //         // "$connectors-activation",
    //         // // reacts to connector events: activating, deactivating, etc.
    //         // "$connectors-"
    //     ];
    //
    //     var processor = SystemProcessor.Builder
    //         .ProcessorName("connectors-coordinator")
    //         .Streams(streams)
    //         .DefaultOutputStream("$connectors-activation-requests")
    //         .InitialPosition(SubscriptionInitialPosition.Latest)
    //         .DisableAutoCommit()
    //         .Publisher(publisher)
    //         .LoggerFactory(loggerFactory)
    //         .EnableLogging()
    //         .WithModule(coordinator)
    //         .Create();
    //
    //     return processor;
    // }
}