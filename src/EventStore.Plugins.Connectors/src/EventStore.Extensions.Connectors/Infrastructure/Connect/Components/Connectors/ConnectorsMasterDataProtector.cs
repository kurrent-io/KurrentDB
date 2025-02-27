// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Connectors.Connect.Components.Connectors;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Kurrent.Toolkit;
using Microsoft.Extensions.Configuration;

namespace EventStore.Connect.Connectors;

public interface IConnectorsMasterDataProtector : IConnectorDataProtector;

[PublicAPI]
public class ConnectorsMasterDataProtector : IConnectorsMasterDataProtector {
    public static readonly ConnectorsMasterDataProtector Instance = new();

    public async ValueTask<IDictionary<string, string?>> Protect(
        string connectorId, IDictionary<string, string?> settings, IDataProtector? dataProtector, CancellationToken ct = default
    ) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var connectorTypeName = configuration
            .GetRequiredOptions<ConnectorOptions>()
            .InstanceTypeName;

        if (!TryGetConnectorDataProtector(connectorTypeName, out var protector))
            throw new DataProtectionException($"Could not find data protector for connector type {connectorTypeName}");

        return await protector.Protect(connectorId, settings, dataProtector, ct);
    }

    public async ValueTask<IConfiguration> Unprotect(IConfiguration configuration, IDataProtector? dataProtector, CancellationToken ct = default) {
        var connectorTypeName = configuration
            .GetRequiredOptions<ConnectorOptions>()
            .InstanceTypeName;

        if (!TryGetConnectorDataProtector(connectorTypeName, out var protector))
            throw new DataProtectionException($"Could not find data protector for connector type {connectorTypeName}");

        return await protector.Unprotect(configuration, dataProtector, ct);
    }

    protected virtual bool TryGetConnectorDataProtector(
        ConnectorInstanceTypeName connectorTypeName,
        out IConnectorDataProtector protector
    ) {
        if (string.IsNullOrWhiteSpace(connectorTypeName))
            throw new DataProtectionException("Failed to extract connector instance type name from configuration");

        protector = null!;

        if (!ConnectorCatalogue.TryGetConnector(connectorTypeName, out var connector))
            return false;

        protector = (Activator.CreateInstance(connector.ConnectorProtectorType) as IConnectorDataProtector)!;
        return true;
    }
}