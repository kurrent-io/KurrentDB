// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace KurrentDB.Surge.Testing.TUnit;

public static class ApplicationContext {
    public static IConfiguration Configuration { get; private set; } = null!;

    public static void Initialize() {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        Configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)                    // Accept default naming convention
            .AddJsonFile($"appsettings.{environment.ToLowerInvariant()}.json", optional: true) // Linux is case-sensitive
            .AddEnvironmentVariables()
            .Build();
    }
}
