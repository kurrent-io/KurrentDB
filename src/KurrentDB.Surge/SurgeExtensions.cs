// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Consumers.Configuration;
using KurrentDB.Core.Bus;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers.Configuration;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Surge.Consumers;
using KurrentDB.Surge.Processors;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Surge;

public static class SurgeExtensions {
    public static IServiceCollection AddSurgeSystemComponents(this IServiceCollection services) {
        services.AddSingleton<IStateStore, InMemoryStateStore>();

        services.AddSingleton<IReaderBuilder, SystemReaderBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemReader.Builder
	            .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IConsumerBuilder, SystemConsumerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemConsumer.Builder
	            .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProducerBuilder, SystemProducerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemProducer.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProcessorBuilder, SystemProcessorBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var stateStore     = ctx.GetRequiredService<IStateStore>();
            var publisher      = ctx.GetRequiredService<IPublisher>();

            return SystemProcessor.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .StateStore(stateStore)
                .LoggerFactory(loggerFactory);
        });

        services.AddSingleton<SystemManager>(ctx => new SystemManager(ctx.GetRequiredService<IPublisher>()));

        return services;
    }

    public static IServiceCollection AddSurgeSchemaRegistry(this IServiceCollection services, SchemaRegistry schemaRegistry) =>
        services
            .AddSingleton(schemaRegistry)
            .AddSingleton<ISchemaRegistry>(schemaRegistry)
            .AddSingleton<ISchemaSerializer>(schemaRegistry);
}
