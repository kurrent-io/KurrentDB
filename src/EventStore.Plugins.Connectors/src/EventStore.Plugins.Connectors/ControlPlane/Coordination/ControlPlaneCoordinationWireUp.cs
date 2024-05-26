using EventStore.Connectors.ControlPlane.Assignment;
using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Hosting;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.ControlPlane.Coordination;

public static class ControlPlaneCoordinationWireUp {
    public static void AddConnectorsCoordination(this IServiceCollection services) {
       
        services.AddSingleton<ConnectorsManagementClient>();
        
        services.AddSingleton<ConnectorAssignorProvider>();
        
        services.AddHostedService(ctx => new ProcessorWorker<SystemProcessor>(CreateProcessor(ctx), ctx));
    }
    
    public static SystemProcessor CreateProcessor(IServiceProvider serviceProvider) {
        var publisher              = serviceProvider.GetRequiredService<IPublisher>();
        var loggerFactory          = serviceProvider.GetRequiredService<ILoggerFactory>();
        var timeProvider           = serviceProvider.GetRequiredService<TimeProvider>();
        var assignorProvider       = serviceProvider.GetRequiredService<ConnectorAssignorProvider>();
        var managementClient       = serviceProvider.GetRequiredService<ConnectorsManagementClient>();
        
        var coordinator = new ConnectorsCoordinator(assignorProvider.Get, managementClient.ListActiveConnectors);

        string[] streams = [
            // reacts to cluster topology changes
            SystemStreams.GossipStream,
            // reacts to task manager events: started, stopped, etc.
            "$connectors-activation",
            // reacts to connector events: activating, deactivating, etc.
            "$connectors-"
        ];

        var processor = SystemProcessor.Builder
            .ProcessorName("connectors-coordinator")
            .Streams(streams)
            .DefaultOutputStream("$connectors-activation-requests")
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .DisableAutoCommit()
            .Publisher(publisher)
            .LoggerFactory(loggerFactory)
            .EnableLogging()
            .WithModule(coordinator)
            .Create();

        return processor;
    }
}