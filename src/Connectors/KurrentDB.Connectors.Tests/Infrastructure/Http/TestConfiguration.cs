// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace KurrentDB.Connectors.Tests.Infrastructure.Http;

public static class TestConfiguration {
    // This async local is set in from tests, and it flows to main
    static readonly AsyncLocal<Action<IConfigurationBuilder>?> Current = new();

    /// <summary>
    /// Adds the current test configuration to the application in the "right" place
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder</param>
    /// <returns>The modified <see cref="IConfigurationBuilder"/></returns>
    public static IConfigurationBuilder AddTestConfiguration(this IConfigurationBuilder configurationBuilder) {
        if (Current.Value is { } configure)
            configure(configurationBuilder);

        return configurationBuilder;
    }

    /// <summary>
    /// Unit tests can use this to flow state to the main program and change configuration
    /// </summary>
    public static void Create(Action<IConfigurationBuilder> action) => Current.Value = action;
}
