// // ReSharper disable CheckNamespace
//
// using EventStore.IO.Connectors;
// using EventStore.Streaming.Hosting;
// using Microsoft.Extensions.DependencyInjection;
//
// namespace EventStore.Streaming.Connectors;
//
// public static class EventStoreBuilderExtensions {
//     public static EventStoreStreamingBuilder AddConnectors(this EventStoreStreamingBuilder builder) {
//         builder.AddSinkConnectorFactory();
//         return builder;
//     }
//
//     public static EventStoreStreamingBuilder AddSinkConnectorFactory(this EventStoreStreamingBuilder builder) {
//         builder.Services.AddSingleton<IConnectorsFactory>(ctx => new SystemConnectorsFactory(ctx));
//         return builder;
//     }
// }