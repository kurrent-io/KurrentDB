// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace KurrentDB.Connectors.Infrastructure;

public static class ConfigurationSettingsExtensions {
    public static IConfiguration ToConfiguration(this IDictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    public static IDictionary<string, string?> ToSettings(this IConfiguration configuration) =>
        new Dictionary<string, string?>(configuration.AsEnumerable().Where(x => x.Value is not null));
}