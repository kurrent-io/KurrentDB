// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Microsoft.Extensions.Configuration;

namespace EventStore.Connectors.Connect.Components.Connectors;

public interface IConnectorDataProtector {
    public HashSet<string> SensitiveKeys { get; }

    ValueTask<IDictionary<string, string?>> Protect(
        string connectorId, IDictionary<string, string?> settings, CancellationToken ct
    );

    ValueTask<IConfiguration> Unprotect(
        IConfiguration configuration, CancellationToken ct
    );

    IDictionary<string, string?> Protect(string connectorId, IDictionary<string, string?> settings) =>
        Protect(connectorId, settings, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    IConfiguration Unprotect(IConfiguration configuration) =>
        Unprotect(configuration, CancellationToken.None).AsTask().GetAwaiter().GetResult();
}

public abstract class ConnectorDataProtector<T> : IConnectorDataProtector where T : class, IConnectorOptions {
    protected ConnectorDataProtector(IDataProtector dataProtector) {
        DataProtector = dataProtector;
        SensitiveKeys = new HashSet<string>(ConfigureSensitiveKeys(), StringComparer.OrdinalIgnoreCase);
    }

    IDataProtector DataProtector { get; }

    public HashSet<string> SensitiveKeys { get; }

    protected abstract string[] ConfigureSensitiveKeys();

    public async ValueTask<IDictionary<string, string?>> Protect(string connectorId, IDictionary<string, string?> settings, CancellationToken ct) {
        if(SensitiveKeys.Count == 0 || settings.Count == 0)
            return settings;

        foreach (var (key, value) in settings) {
            // Use the case-insensitive SensitiveKeys.Contains
            if (SensitiveKeys.Contains(key) && !string.IsNullOrEmpty(value)) {
                settings[key] = await DataProtector.Protect(value, keyIdentifier: $"{connectorId}:{key}", ct);
            }
        }

        return settings;
    }

    public async ValueTask<IConfiguration> Unprotect(IConfiguration configuration, CancellationToken ct) {
        if(SensitiveKeys.Count == 0)
            return configuration;

        foreach (var (key, value) in configuration.AsEnumerable()) {
            if (SensitiveKeys.Contains(key) && !string.IsNullOrEmpty(value)) {
                var plaintext = await DataProtector.Unprotect(value, ct);
                configuration[key] = plaintext;
            }
        }

        return configuration;
    }
}