// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers.Configuration;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core;
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
            var client         = ctx.GetRequiredService<ISystemClient>();

            return SystemReader.Builder
	            .Client(client)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IConsumerBuilder, SystemConsumerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var client         = ctx.GetRequiredService<ISystemClient>();

            return SystemConsumer.Builder
	            .Client(client)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProducerBuilder, SystemProducerBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var client         = ctx.GetRequiredService<ISystemClient>();

            return SystemProducer.Builder
                .Client(client)
                .SchemaRegistry(schemaRegistry)
                .LoggerFactory(loggerFactory)
                .DisableResiliencePipeline();
        });

        services.AddSingleton<IProcessorBuilder, SystemProcessorBuilder>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var stateStore     = ctx.GetRequiredService<IStateStore>();
            var client         = ctx.GetRequiredService<ISystemClient>();

            return SystemProcessor.Builder
                .Client(client)
                .SchemaRegistry(schemaRegistry)
                .StateStore(stateStore)
                .LoggerFactory(loggerFactory);
        });

        services.AddSingleton<SystemManager>(ctx => new SystemManager(ctx.GetRequiredService<ISystemClient>()));

        return services;
    }

    public static IServiceCollection AddSurgeSchemaRegistry(this IServiceCollection services, SchemaRegistry schemaRegistry) =>
        services
            .AddSingleton(schemaRegistry)
            .AddSingleton<ISchemaRegistry>(schemaRegistry)
            .AddSingleton<ISchemaSerializer>(schemaRegistry);
}
