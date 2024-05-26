using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Connectors;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Hosting;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.ControlPlane.Activation;

public static class ControlPlaneActivationWireUp {
    public static void AddConnectorsActivation(this IServiceCollection services) {
        services.AddSingleton(ctx => new ConnectorsTaskManager(new SystemConnectorsFactory(ctx)));
        
        services.AddHostedService(ctx => new ProcessorWorker<SystemProcessor>(CreateProcessor(ctx), ctx));
    }
    
    public static SystemProcessor CreateProcessor(IServiceProvider serviceProvider) {
        var publisher              = serviceProvider.GetRequiredService<IPublisher>();
        var loggerFactory          = serviceProvider.GetRequiredService<ILoggerFactory>();
        var timeProvider           = serviceProvider.GetRequiredService<TimeProvider>();
        var taskManager            = serviceProvider.GetRequiredService<ConnectorsTaskManager>();

        var activator = new ConnectorsActivator(taskManager, timeProvider);

        var processor = SystemProcessor.Builder
            .ProcessorName("connectors-activator")
            .Streams("$connectors-activation-requests")
            .DefaultOutputStream("$connectors-activation")
            .InitialPosition(SubscriptionInitialPosition.Earliest)
            .DisableAutoCommit()
            .Publisher(publisher)
            .LoggerFactory(loggerFactory)
            .EnableLogging()
            .WithModule(activator)
            .Create();

        return processor;
    }
}