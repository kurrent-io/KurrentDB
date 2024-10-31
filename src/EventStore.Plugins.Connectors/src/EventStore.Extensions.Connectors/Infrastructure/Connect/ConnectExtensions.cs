// ReSharper disable CheckNamespace

using EventStore.Connect.Connectors;
using EventStore.Connect.Consumers;
using EventStore.Connect.Consumers.Configuration;
using EventStore.Connect.Processors;
using EventStore.Connect.Processors.Configuration;
using EventStore.Connect.Producers;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Connect.Components.Producers;
using EventStore.Core.Bus;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Producers.Configuration;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connect;

public static class ConnectExtensions {
    public static IServiceCollection AddConnectSystemComponents(this IServiceCollection services) {
        services.AddConnectSchemaRegistry(SchemaRegistry.Global);

        services.AddSingleton<IStateStore, InMemoryStateStore>();

        services.AddSingleton<Func<SystemReaderBuilder>>(ctx => {
            var publisher      = ctx.GetRequiredService<IPublisher>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => SystemReader.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "EventStore.Connect.SystemReader"
                });
        });

        services.AddSingleton<Func<SystemConsumerBuilder>>(ctx => {
            var publisher      = ctx.GetRequiredService<IPublisher>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => SystemConsumer.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "EventStore.Connect.SystemConsumer"
                });
        });

        services.AddSingleton<Func<SystemProducerBuilder>>(ctx => {
            var publisher      = ctx.GetRequiredService<IPublisher>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => SystemProducer.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "EventStore.Connect.SystemProducer"
                });
        });

        services.AddSingleton<IProducerProvider, SystemProducerProvider>();

        services.AddSingleton<Func<SystemProcessorBuilder>>(ctx => {
            var publisher      = ctx.GetRequiredService<IPublisher>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var stateStore     = ctx.GetRequiredService<IStateStore>();

            return () => SystemProcessor.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .StateStore(stateStore)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "EventStore.Connect.SystemProcessor"
                });
        });

        services.AddSingleton<IConnectorValidator, SystemConnectorsValidation>();
        services.AddSingleton<IConnectorFactory, SystemConnectorsFactory>();
        services.AddSingleton<Func<GrpcProducerBuilder>>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => GrpcProducer.Builder
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "EventStore.Streaming.GrpcProducer"
                });
        });

        return services;
    }

    public static IServiceCollection AddConnectSchemaRegistry(this IServiceCollection services, SchemaRegistry? schemaRegistry = null) {
        schemaRegistry ??= SchemaRegistry.Global;

        return services
            .AddSingleton(schemaRegistry)
            .AddSingleton<ISchemaRegistry>(schemaRegistry)
            .AddSingleton<ISchemaSerializer>(schemaRegistry);
    }
}