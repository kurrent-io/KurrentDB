// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Api.Infrastructure;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddNodeSystemInfoProvider(this IServiceCollection services) =>
        services.AddSingleton<INodeSystemInfoProvider, NodeSystemInfoProvider>();
}
