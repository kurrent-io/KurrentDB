using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KurrentDB.Api.Infrastructure.DependencyInjection;

public static class OptionsBuilderExtensions {
    public static IServiceCollection Configure<TOptions>(this IServiceCollection services, Action<IServiceProvider, TOptions> configure) where TOptions : class =>
        services.AddSingleton<IConfigureOptions<TOptions>>(sp => new ConfigureNamedOptions<TOptions>(null, options => configure(sp, options)));

    public static IServiceCollection PostConfigure<TOptions>(this IServiceCollection services, Action<IServiceProvider, TOptions> configure) where TOptions : class =>
        services.AddSingleton<IPostConfigureOptions<TOptions>>(sp => new PostConfigureOptions<TOptions>(null, options => configure(sp, options)));
}
