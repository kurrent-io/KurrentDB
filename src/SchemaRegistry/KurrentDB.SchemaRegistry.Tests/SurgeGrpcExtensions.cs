using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.Managers;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers;
using Kurrent.Surge.Readers.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SchemaRegistry.Tests;

public static class SurgeGrpcExtensions {
    public static IServiceCollection AddSurgeGrpcComponents(this IServiceCollection services) {
        var clientSettings = KurrentDbClientSettings.Default;

        services.AddSingleton<IStateStore, InMemoryStateStore>();

        services.AddSingleton<IReaderBuilder, GrpcReaderBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();

            return GrpcReader.Builder
                .ClientSettings(clientSettings)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IConsumerBuilder, GrpcConsumerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();

            return GrpcConsumer.Builder
                .ClientSettings(clientSettings)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProducerBuilder, GrpcProducerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();

            return GrpcProducer.Builder
                .ClientSettings(clientSettings)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProcessorBuilder, GrpcProcessorBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>();
            var stateStore     = ctx.GetRequiredService<IStateStore>();

            return GrpcProcessor.Builder
                .ClientSettings(clientSettings)
                .SchemaRegistry(schemaRegistry)
                .StateStore(stateStore)
                .LoggerFactory(loggerFactory);
        });

        services.AddSingleton<IManagerBuilder, GrpcManagerBuilder>(ctx => {
            var loggerFactory = ctx.GetRequiredService<ILoggerFactory>();

            return GrpcManager.Builder
                .ClientSettings(clientSettings)
                .LoggerFactory(loggerFactory);
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