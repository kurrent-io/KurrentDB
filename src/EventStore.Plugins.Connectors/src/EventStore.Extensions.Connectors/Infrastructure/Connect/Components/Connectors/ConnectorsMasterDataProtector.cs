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
public class ConnectorsMasterDataProtector(IDataProtector dataProtector, DataProtectionOptions options) : IConnectorDataProtector {
    IDataProtector        DataProtector { get; } = dataProtector;
    DataProtectionOptions Options       { get; } = options;

    ConcurrentDictionary<ConnectorInstanceTypeName, IConnectorDataProtector> Protectors { get; } = new();

    public HashSet<string> SensitiveKeys { get; } = [];

    public async ValueTask<IDictionary<string, string?>> Protect(
        string connectorId, IDictionary<string, string?> settings, CancellationToken ct = default
    ) {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var protector = Protectors.GetOrAdd(
            GetConnectorTypeName(configuration),
            static (alias, protector) => ConnectorCatalogue.TryGetConnector(alias, out var info)
                ? (IConnectorDataProtector)CreateInstance(info.ConnectorProtectorType, protector)!
                : throw new DataProtectionException($"Failed to find data protector for connector {alias}"),
            DataProtector
        );

        // Find any sensitive keys present in the settings
        var presentSensitiveKeys = protector.SensitiveKeys.Intersect(settings.Keys, StringComparer.OrdinalIgnoreCase).Select(x => $"[{x}]").ToArray();
        var hasSensitiveKeys     = presentSensitiveKeys.Length > 0;

        if (hasSensitiveKeys && Options.Token == DataProtectionConstants.NoOpToken)
            throw new DataProtectionException(
                $"Data protection token not found!{Environment.NewLine}"
              + $"Sensitive data keys found: {string.Join(", ", presentSensitiveKeys)}{Environment.NewLine}"
              + $"Please check the documentation for instructions on how to configure the token.");

        return await protector.Protect(connectorId, settings, ct);
    }

    public async ValueTask<IConfiguration> Unprotect(IConfiguration configuration, CancellationToken ct = default) {
        // bypass the unprotection if the token is not set
        // this is to allow for backwards compatibility
        if (Options.Token == DataProtectionConstants.NoOpToken)
            return configuration;

        var protector = Protectors.GetOrAdd(
            GetConnectorTypeName(configuration),
            static (alias, protector) => ConnectorCatalogue.TryGetConnector(alias, out var info)
                ? (IConnectorDataProtector)CreateInstance(info.ConnectorProtectorType, protector)!
                : throw new DataProtectionException($"Failed to find data protector for connector {alias}"),
            DataProtector
        );

        // even if a token is set, the config might not be protected,
        // so if we get a FormatException, we return the config as is
        // this is to allow for backwards compatibility
        try {
            return await protector.Unprotect(configuration, ct);
        }
        catch (FormatException)  {
            // "Invalid Base64Url encoded string."
            return configuration;
        }
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