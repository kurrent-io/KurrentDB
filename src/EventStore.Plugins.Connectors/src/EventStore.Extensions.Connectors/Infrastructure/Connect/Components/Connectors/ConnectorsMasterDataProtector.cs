// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Concurrent;
using EventStore.Connectors.Connect.Components.Connectors;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Kurrent.Toolkit;
using Microsoft.Extensions.Configuration;
using static System.Activator;

namespace EventStore.Connect.Connectors;

[PublicAPI]
public class ConnectorsMasterDataProtector(IDataProtector dataProtector) : IConnectorDataProtector {
    IDataProtector DataProtector { get; } = dataProtector;

    ConcurrentDictionary<ConnectorInstanceTypeName, IConnectorDataProtector> Protectors { get; } = new();

    public async ValueTask<IDictionary<string, string?>> Protect(
        string connectorId, IDictionary<string, string?> settings, CancellationToken ct = default
    ) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var protector = Protectors.GetOrAdd(
            GetConnectorTypeName(configuration),
            static (alias, protector) => ConnectorCatalogue.TryGetConnector(alias, out var info)
                ? (IConnectorDataProtector)CreateInstance(info.ConnectorProtectorType, protector)!
                : throw new DataProtectionException($"Failed to find data protector for connector type {alias}"),
            DataProtector
        );

        return await protector.Protect(connectorId, settings, ct);
    }

    public async ValueTask<IConfiguration> Unprotect(IConfiguration configuration, CancellationToken ct = default) {
        var protector = Protectors.GetOrAdd(
            GetConnectorTypeName(configuration),
            static (alias, protector) => ConnectorCatalogue.TryGetConnector(alias, out var info)
                ? (IConnectorDataProtector)CreateInstance(info.ConnectorProtectorType, protector)!
                : throw new DataProtectionException($"Failed to find data protector for connector type {alias}"),
            DataProtector
        );

        return await protector.Unprotect(configuration, ct);
    }

    static string GetConnectorTypeName(IConfiguration configuration) {
        var connectorTypeName = configuration
            .GetRequiredOptions<ConnectorOptions>()
            .InstanceTypeName;

        return string.IsNullOrWhiteSpace(connectorTypeName)
            ? throw new DataProtectionException("Failed to extract connector instance type name from configuration")
            : connectorTypeName;
    }
}