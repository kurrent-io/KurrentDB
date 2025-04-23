// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace KurrentDB.Connectors.Infrastructure;

public static class ConfigurationSettingsExtensions {
    public static IConfiguration ToConfiguration(this IDictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    public static IDictionary<string, string?> ToSettings(this IConfiguration configuration) =>
        new Dictionary<string, string?>(configuration.AsEnumerable().Where(x => x.Value is not null));
}