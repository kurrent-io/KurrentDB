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
using EventStore.Connect.Schema;
using EventStore.Connectors.Connect.Components.Producers;
using EventStore.Connectors.Infrastructure;
using EventStore.Core.Bus;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Kurrent.Surge.DataProtection.Vaults;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using Kurrent.Toolkit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EncryptionKey = Kurrent.Surge.DataProtection.Protocol.EncryptionKey;
using LoggingOptions = Kurrent.Surge.Configuration.LoggingOptions;

namespace EventStore.Connect;

public static class SurgeExtensions {
    public static IServiceCollection AddSurgeSystemComponents(this IServiceCollection services) {
        services.AddSurgeSchemaRegistry(SchemaRegistry.Global);

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
                    LogName       = "Kurrent.Surge.SystemReader"
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
                    LogName       = "Kurrent.Surge.SystemConsumer"
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
                    LogName       = "Kurrent.Surge.SystemProducer"
                });
        });

        services.AddSingleton<Func<SystemProcessorBuilder>>(ctx => {
            var publisher      = ctx.GetRequiredService<IPublisher>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var stateStore     = ctx.GetRequiredService<IStateStore>();

            return () => SystemProcessor.Builder
                .Publisher(publisher)
                .SchemaRegistry(schemaRegistry)
                .StateStore(stateStore)
                .Logging(new LoggingOptions {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "Kurrent.Surge.SystemProcessor"
                });
        });

        services.AddSingleton<IConnectorValidator, SystemConnectorsValidation>();

        // this is for the prototype kurrent db sink
        services.AddSingleton<IProducerProvider, SystemProducerProvider>();

        services.AddSingleton<Func<GrpcProducerBuilder>>(ctx => {
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => GrpcProducer.Builder
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "Kurrent.Surge.GrpcProducer"
                });
        });

        return services;
    }

    public static IServiceCollection AddSurgeSchemaRegistry(this IServiceCollection services, SchemaRegistry schemaRegistry) =>
        services
            .AddSingleton(schemaRegistry)
            .AddSingleton<ISchemaRegistry>(schemaRegistry)
            .AddSingleton<ISchemaSerializer>(schemaRegistry);

    public static IServiceCollection AddSurgeDataProtection(this IServiceCollection services, IConfiguration configuration) {
        string[] sections = [
            "KurrentDB:Connectors:DataProtection",
            "KurrentDB:DataProtection",

            "Kurrent:Connectors:DataProtection",
            "Kurrent:DataProtection",

            "EventStore:Connectors:DataProtection",
            "EventStore:DataProtection",

            "Connectors:DataProtection",
            "DataProtection"
        ];

        var config = configuration
            .GetFirstExistingSection("DataProtection", sections);

        services
            .AddDataProtection(config)
            .ProtectKeysWithToken()
            .PersistKeysToSurge();

        return services;
    }

    static DataProtectionBuilder ProtectKeysWithToken(this DataProtectionBuilder builder) {
        // string[] sections = [
        //     "KurrentDB:Connectors:DataProtection",
        //     "KurrentDB:DataProtection",
        //
        //     "Kurrent:Connectors:DataProtection",
        //     "Kurrent:DataProtection",
        //
        //     "EventStore:Connectors:DataProtection",
        //     "EventStore:DataProtection",
        //
        //     "Connectors:DataProtection",
        //     "DataProtection"
        // ];

        var options = builder.Configuration
            // .GetFirstExistingSection("DataProtection", sections)
            .GetOptionsOrDefault<DataProtectionOptions>();

        if (options.HasTokenFile) {
            try {
                return builder.ProtectKeysWithToken(File.ReadAllText(options.TokenFile!));
            }
            catch (Exception ex) {
                throw new InvalidOperationException($"Failed to load data protection token from file: {options.TokenFile}", ex);
            }
        }

        return options.HasToken
            ? builder.ProtectKeysWithToken(options.Token!)
            : throw new InvalidOperationException("Data protection token not found!");
    }

    static DataProtectionBuilder PersistKeysToSurge(this DataProtectionBuilder builder) {
        builder.Services.AddSingleton<IReader>(ctx => {
            var factory = ctx.GetRequiredService<Func<SystemReaderBuilder>>();
            return factory().ReaderId("Kurrent.Surge.DataProtection.Reader").Create();
        });

        builder.Services.AddSingleton<IProducer>(ctx => {
            var factory = ctx.GetRequiredService<Func<SystemProducerBuilder>>();
            return factory().ProducerId("Kurrent.Surge.DataProtection.Producer").Create();
        });

        builder.Services.AddSingleton<IManager>(ctx => {
            var manager = new SystemManager(ctx.GetRequiredService<IPublisher>());
            return manager;
        });

        builder.Services.AddSchemaRegistryStartupTask(
            "Data Protection Schema Registration",
            static async (registry, token) => {
                var schemaInfo = new SchemaInfo("$data-protection-encryption-key", SchemaDefinitionType.Json);
                await registry.RegisterSchema<EncryptionKey>(schemaInfo, cancellationToken: token);
            }
        );

        // string[] sections = [
        //     "KurrentDB:Connectors:DataProtection:KeyVaults:Surge",
        //     "KurrentDB:DataProtection:KeyVaults:Surge",
        //
        //     "Kurrent:Connectors:DataProtection:KeyVaults:Surge",
        //     "Kurrent:DataProtection:KeyVaults:Surge",
        //
        //     "EventStore:Connectors:DataProtection:KeyVaults:Surge",
        //     "EventStore:DataProtection:KeyVaults:Surge",
        //
        //     "Connectors:DataProtection:KeyVaults:Surge",
        //     "DataProtection:KeyVaults:Surge"
        // ];

        var options = builder.Configuration
            .GetFirstExistingSection("Surge", "KeyVaults:Surge")
            .GetOptionsOrDefault<SurgeKeyVaultOptions>();

        builder.Services
            .OverrideSingleton(options)
            .OverrideSingleton<SurgeKeyVault>()
            .OverrideSingleton<IKeyVault>(ctx => ctx.GetRequiredService<SurgeKeyVault>());

        return builder.PersistKeysToSurge(options);
    }
}