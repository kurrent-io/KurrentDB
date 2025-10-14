// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Connect.Connectors;
using KurrentDB.Connect.Schema;
using KurrentDB.Core;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Kurrent.Surge.DataProtection.Cryptography;
using Kurrent.Surge.DataProtection.Vaults;
using Kurrent.Surge.Persistence.State;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Surge;
using KurrentDB.Surge.Consumers;
using KurrentDB.Surge.Processors;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using EncryptionKey = Kurrent.Surge.DataProtection.Protocol.EncryptionKey;
using LoggingOptions = Kurrent.Surge.Configuration.LoggingOptions;

namespace KurrentDB.Connect;

public static class SurgeExtensions {
    public static IServiceCollection AddSurgeSystemComponents(this IServiceCollection services) {
        services.AddSingleton<IStateStore, InMemoryStateStore>();

        services.AddSingleton<Func<SystemReaderBuilder>>(ctx => {
            var client         = ctx.GetRequiredService<ISystemClient>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => SystemReader.Builder
                .Client(client)
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "Kurrent.Surge.SystemReader"
                });
        });

        services.AddSingleton<Func<SystemConsumerBuilder>>(ctx => {
            var client         = ctx.GetRequiredService<ISystemClient>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => SystemConsumer.Builder
                .Client(client)
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "Kurrent.Surge.SystemConsumer"
                });
        });

        services.AddSingleton<Func<SystemProducerBuilder>>(ctx => {
            var client         = ctx.GetRequiredService<ISystemClient>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();

            return () => SystemProducer.Builder
                .Client(client)
                .SchemaRegistry(schemaRegistry)
                .Logging(new() {
                    Enabled       = true,
                    LoggerFactory = loggerFactory,
                    LogName       = "Kurrent.Surge.SystemProducer"
                });
        });

        services.AddSingleton<Func<SystemProcessorBuilder>>(ctx => {
            var client         = ctx.GetRequiredService<ISystemClient>();
            var loggerFactory  = ctx.GetRequiredService<ILoggerFactory>();
            var schemaRegistry = ctx.GetRequiredService<SchemaRegistry>();
            var stateStore     = ctx.GetRequiredService<IStateStore>();

            return () => SystemProcessor.Builder
                .Client(client)
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

    public static IServiceCollection AddSurgeSchemaRegistry(this IServiceCollection services, SchemaRegistry? schemaRegistry = null) {
		schemaRegistry ??= SchemaRegistry.Global;

	    return services
		    .AddSingleton(schemaRegistry)
		    .AddSingleton<ISchemaRegistry>(schemaRegistry)
		    .AddSingleton<ISchemaSerializer>(schemaRegistry);
    }

    public static IServiceCollection AddSurgeDataProtection(this IServiceCollection services, IConfiguration configuration) {
        var config = configuration
            .GetFirstExistingSection("KurrentDB", "KurrentDB", "Kurrent", "EventStore");

        services
            .AddDataProtection(config)
            .ProtectKeysWithToken()
            .PersistKeysToSurge();

        return services;
    }

    static DataProtectionBuilder AddDataProtection(this IServiceCollection services, IConfiguration configuration) {
        string[] sections = [
            "Kurrent:Connectors:DataProtection",
            "Kurrent:DataProtection",
            "Connectors:DataProtection",
            "DataProtection"
        ];

        var config = configuration
            .GetFirstExistingSection("DataProtection", sections);

        var options = config.GetOptionsOrDefault<DataProtectionOptions>();

        if (options.Token is null && options.TokenFile is null)
            config["Token"] = options.Token = DataProtectionConstants.NoOpToken;

        services
            .Configure<DataProtectionOptions>(config)
            .AddSingleton<DataProtectionOptions>(ctx => ctx.GetRequiredService<IOptions<DataProtectionOptions>>().Value);

        services.AddSingleton<DataProtectorOptions>(ctx => new() {
            Destroy = ctx.GetRequiredService<DataProtectionOptions>().Destroy
        });

        services.AddSingleton<IDataProtector, DataProtector>();
        services.AddSingleton<IDataEncryptor, AesGcmDataEncryptor>();

        return new DataProtectionBuilder(services, config);
    }

    static DataProtectionBuilder ProtectKeysWithToken(this DataProtectionBuilder builder) {
        var options = builder.Configuration
            .GetOptionsOrDefault<DataProtectionOptions>();

        if (options.HasTokenFile) {
            try {
                return builder.ProtectKeysWithToken(File.ReadAllText(options.TokenFile!));
            }
            catch (Exception ex) {
                throw new InvalidOperationException($"Failed to load data protection token from file: {options.TokenFile}", ex);
            }
        }

        // it is necessary to have a token, but we will check the data protection options
        // later to understand when it is necessary to throw an exception when trying to
        // protect sensitive data.
        return builder.ProtectKeysWithToken(options.Token ?? DataProtectionConstants.NoOpToken);
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
            var manager = new SystemManager(ctx.GetRequiredService<ISystemClient>());
            return manager;
        });

        builder.Services.AddSchemaRegistryStartupTask(
            "Data Protection Schema Registration",
            static async (registry, token) => {
                var schemaInfo = new SchemaInfo("$data-protection-encryption-key", SchemaDataFormat.Json);
                await registry.RegisterSchema<EncryptionKey>(schemaInfo, cancellationToken: token);
            }
        );

        var settings = builder.Configuration.GetSection("KeyVaults:Surge");
        var options  = settings.GetOptionsOrDefault<SurgeKeyVaultOptions>();

        // force the ProtobufEncoding to false when not set
        // this will be ported to Surge later
        options.ProtobufEncoding = settings.GetValue("ProtobufEncoding", false);

        builder.Services
            .OverrideSingleton(options)
            .OverrideSingleton<SurgeKeyVault>()
            .OverrideSingleton<IKeyVault>(ctx => ctx.GetRequiredService<SurgeKeyVault>());

        return builder.PersistKeysToSurge(options);
    }
}
