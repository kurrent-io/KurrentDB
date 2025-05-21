using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers.Configuration;
using KurrentDB.Core.Bus;
using KurrentDB.Surge;
using KurrentDB.Surge.Consumers;
using KurrentDB.Surge.Processors;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SchemaRegistry.Tests;

public static class SurgeExtensions {
    public static IServiceCollection AddSurgeSystemComponents(this IServiceCollection services) {
        services.AddSingleton<IStateStore, InMemoryStateStore>();

        services.AddSingleton<IReaderBuilder, SystemReaderBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemReader.Builder
	            .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IConsumerBuilder, SystemConsumerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemConsumer.Builder
	            .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProducerBuilder, SystemProducerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemProducer.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProcessorBuilder, SystemProcessorBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();
            var stateStore     = ctx.GetRequiredService<IStateStore>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemProcessor.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .StateStore(stateStore)
                .LoggerFactory(loggerFactory);
        });

        services.AddSingleton<SystemManager>(ctx => {
            // var loggerFactory = ctx.GetRequiredService<ILoggerFactory>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return new SystemManager(publisher: publisher);
        });

        return services;
    }

    // public static IServiceCollection AddConnectSchemaRegistry(this IServiceCollection services, Surge.Schema.SchemaRegistry? schemaRegistry = null) {
    //     schemaRegistry ??= Surge.Schema.SchemaRegistry.Global;
    //
    //     return services
    //         .AddSingleton(schemaRegistry)
    //         .AddSingleton<ISchemaRegistry>(schemaRegistry)
    //         .AddSingleton<ISchemaSerializer>(schemaRegistry);
    // }
}
