// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Connect.Connectors;
using EventStore.Connect.Schema;
using EventStore.Connectors.Connect.Components.Connectors;
using EventStore.Connectors.Management;
using Kurrent.Surge.DataProtection;
using Kurrent.Surge.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DataProtectionProtocol = Kurrent.Surge.DataProtection.Protocol;
using static System.String;
using static System.StringComparer;

namespace EventStore.Connectors.Infrastructure.Security;

public static class DataProtection {
    public static IServiceCollection AddConnectorsDataProtection(this IServiceCollection services) {
        var serviceProvider = services.BuildServiceProvider();

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var logger        = serviceProvider.GetRequiredService<ILogger<DataProtector>>();

        var token = configuration["KurrentDB:DataProtection:Token"] ?? configuration["EventStore:DataProtection:Token"];

        if (IsNullOrWhiteSpace(token)) {
            logger.LogWarning("DATA PROTECTION IS DISABLED BECAUSE NO TOKEN WAS PROVIDED");
        } else {
            logger.LogInformation("Data protection is enabled");

            services
                .AddMessageSchemaRegistration()
                .AddDataProtection(configuration)
                .ProtectKeysWithToken(token)
                .PersistKeysToSurge();
        }

        services.AddSingleton<ConnectorDomainServices.ProtectConnectorSettings>(ctx => {
            var surgeDataProtector     = ctx.GetService<IDataProtector>();
            var connectorDataProtector = ctx.GetService<IConnectorDataProtector>() ?? ConnectorsMasterDataProtector.Instance;

            return (connectorId, settings) => connectorDataProtector.Protect(
                connectorId,
                settings: new Dictionary<string, string?>(settings, OrdinalIgnoreCase),
                surgeDataProtector
            );
        });

        return services;
    }

    static IServiceCollection AddMessageSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask("Data Protection Schema Registration",
            static async (registry, token) => {
                var schemaInfo = new SchemaInfo(typeof(DataProtectionProtocol.EncryptionKey).FullName!, SchemaDefinitionType.Json);
                await registry.RegisterSchema<DataProtectionProtocol.EncryptionKey>(schemaInfo, cancellationToken: token);
            });
}