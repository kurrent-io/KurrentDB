// using EventStore.Streaming.Hosting;
// using Microsoft.Extensions.DependencyInjection;
//
// namespace EventStore.IO.Connectors;
//
// public static class EventStoreBuilderExtensions {
//     public static EventStoreStreamingBuilder AddConnectors(this EventStoreStreamingBuilder builder) {
//         builder.AddSinkConnectorFactory();
//         return builder;
//     }
//     
//     public static EventStoreStreamingBuilder AddSinkConnectorFactory(this EventStoreStreamingBuilder builder) {
//         builder.Services.AddSingleton<IConnectorsFactory>();
//         return builder;
//     }
// }