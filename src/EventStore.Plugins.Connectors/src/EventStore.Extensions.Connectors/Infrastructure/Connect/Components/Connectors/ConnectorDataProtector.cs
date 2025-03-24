// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Microsoft.Extensions.Configuration;

namespace EventStore.Connectors.Connect.Components.Connectors;

public interface IConnectorDataProtector {
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

public abstract class ConnectorDataProtector<T>(IDataProtector dataProtector) : IConnectorDataProtector where T : class, IConnectorOptions {
    IDataProtector DataProtector { get; } = dataProtector;

    public virtual string[] Keys => [];

    public async ValueTask<IDictionary<string, string?>> Protect(
        string connectorId, IDictionary<string, string?> settings, CancellationToken ct
    ) {
        foreach (var key in Keys) {
            if (!settings.TryGetValue(key, out var plainText) || string.IsNullOrEmpty(plainText))
                continue;

            settings[key] = await DataProtector.Protect(plainText, keyIdentifier: $"{connectorId}:{key}", ct);
        }

        return settings;
    }

    public async ValueTask<IConfiguration> Unprotect(
        IConfiguration configuration, CancellationToken ct
    ) {
        foreach (var key in Keys) {
            var value = configuration[key];

            if (string.IsNullOrEmpty(value)) continue;

            var plaintext = await DataProtector.Unprotect(value, ct);

            configuration[key] = plaintext;
        }

        return configuration;
    }
}