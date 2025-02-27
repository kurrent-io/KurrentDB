// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Microsoft.Extensions.Configuration;

namespace EventStore.Connectors.Connect.Components.Connectors;

public interface IConnectorDataProtector {
    ValueTask<IDictionary<string, string?>> Protect(
        string connectorId, IDictionary<string, string?> settings, IDataProtector? dataProtector, CancellationToken ct = default
    );

    ValueTask<IConfiguration> Unprotect(
        IConfiguration configuration, IDataProtector? dataProtector, CancellationToken ct = default
    );
}

public abstract class ConnectorDataProtector<T> : IConnectorDataProtector where T : class, IConnectorOptions {
    public virtual string[] Keys => [];

    public async ValueTask<IDictionary<string, string?>> Protect(
        string connectorId, IDictionary<string, string?> settings, IDataProtector? dataProtector, CancellationToken ct = default
    ) {
        if (dataProtector is null)
            return settings;

        foreach (var key in Keys) {
            if (!settings.TryGetValue(key, out var plainText) || string.IsNullOrEmpty(plainText))
                continue;

            settings[key] = await dataProtector.Protect(plainText, keyIdentifier: $"{connectorId}:{key}", ct);
        }

        return settings;
    }

    public async ValueTask<IConfiguration> Unprotect(
        IConfiguration configuration, IDataProtector? dataProtector, CancellationToken ct = default
    ) {
        if (dataProtector is null)
            return configuration;

        foreach (var key in Keys) {
            var value = configuration[key];

            if (string.IsNullOrEmpty(value)) continue;

            var plaintext = await dataProtector.Unprotect(value, ct);

            configuration[key] = plaintext;
        }

        return configuration;
    }
}