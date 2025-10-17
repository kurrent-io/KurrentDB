// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KurrentDB.Api.Infrastructure.DependencyInjection;

public static class OptionsBuilderExtensions {
    public static IServiceCollection Configure<TOptions>(this IServiceCollection services, Action<IServiceProvider, TOptions> configure) where TOptions : class =>
        services.AddSingleton<IConfigureOptions<TOptions>>(sp => new ConfigureNamedOptions<TOptions>(null, options => configure(sp, options)));

    public static IServiceCollection PostConfigure<TOptions>(this IServiceCollection services, Action<IServiceProvider, TOptions> configure) where TOptions : class =>
        services.AddSingleton<IPostConfigureOptions<TOptions>>(sp => new PostConfigureOptions<TOptions>(null, options => configure(sp, options)));
}
