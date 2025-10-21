// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace KurrentDB.Testing;

public static class ServiceCollectionExtensions {
    // public static IServiceCollection AddTestLogging(this IServiceCollection services) =>
    //     services.AddLogging(logging => logging.AddProvider(ToolkitTestLoggerProvider.Instance));

    // public static IServiceCollection AddTestLogging(this IServiceCollection services) =>
    //     services.AddLogging(logging => logging.AddSerilog());

    public static IServiceCollection AddTestTimeProvider(this IServiceCollection services, DateTimeOffset? startDateTime = null) =>
        services
            .AddSingleton(new FakeTimeProvider(startDateTime ?? DateTimeOffset.UtcNow))
            .AddSingleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>());
}
