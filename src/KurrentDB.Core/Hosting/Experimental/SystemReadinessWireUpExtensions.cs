using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KurrentDB.Core.Hosting.Experimental;

public static class SystemReadinessWireUpExtensions {
    public static IServiceCollection AddSystemReadiness(this IServiceCollection services) {
        services.AddNodeSystemInfoProvider();
        services.TryAddSingleton<SystemReadiness>();
        return services;
    }
}