using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KurrentDB.Core.Hosting.Experimental;

public static class SystemReadinessWireUpExtensions {
    public static IServiceCollection AddSystemReadiness(this IServiceCollection services) {
        services.AddNodeSystemInfoProvider();
        // Factory on purpose: SystemReadiness has two public constructors and MEDI cannot
        // disambiguate them — plain TryAddSingleton<SystemReadiness>() throws at resolution,
        // which faults host startup and reads as a silent node-readiness timeout.
        services.TryAddSingleton<SystemReadiness>(static sp => new(sp));
        return services;
    }
}