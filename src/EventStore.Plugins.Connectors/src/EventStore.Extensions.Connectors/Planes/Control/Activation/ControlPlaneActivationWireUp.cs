// using EventStore.Core.Bus;
// using EventStore.Streaming.Connectors;
// using EventStore.Streaming.Consumers;
// using EventStore.Streaming.Hosting;
// using EventStore.Streaming.Processors;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
//
// namespace EventStore.Connectors.Control.Activation;
//
// public static class ControlPlaneActivationWireUp {
//     public static void AddConnectorsControlPlaneActivation(this IServiceCollection services) {
//         // services.AddSingleton(ctx => new ConnectorsTaskManager(new SystemConnectorsFactory(ctx)));
//         services.AddSingleton<ConnectorsActivator>();
//
//         services.AddHostedService(ctx => new ProcessorWorker<SystemProcessor>(CreateProcessor(ctx), ctx));
//     }
//
//     public static SystemProcessor CreateProcessor(IServiceProvider serviceProvider) {
//         var publisher     = serviceProvider.GetRequiredService<IPublisher>();
//         var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
//         var activator     = serviceProvider.GetRequiredService<ConnectorsActivator>();
//
//         var module = new ConnectorsActivationModule(activator);
//
//         var processor = SystemProcessor.Builder
//             .ProcessorId("connectors-activator")
//             .Streams("$connectors-activation-requests")
//             // .DefaultOutputStream("$connectors-activation")
//             .InitialPosition(SubscriptionInitialPosition.Earliest)
//             .DisableAutoCommit()
//             .Publisher(publisher)
//             .LoggerFactory(loggerFactory)
//             .EnableLogging()
//             .WithModule(module)
//             .Create();
//
//         return processor;
//     }
// }