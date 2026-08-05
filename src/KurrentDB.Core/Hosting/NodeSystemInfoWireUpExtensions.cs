using System;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KurrentDB.Core.Hosting;

public static class NodeSystemInfoWireUpExtensions {
    public static IServiceCollection AddNodeSystemInfoProvider(this IServiceCollection services) {
        services.TryAddSingleton<GetNodeSystemInfo>(ctx => {
            var publisher = ServiceProviderServiceExtensions.GetRequiredService<IPublisher>(ctx);
            var time      = ServiceProviderServiceExtensions.GetRequiredService<TimeProvider>(ctx);
            return ct => publisher.GetNodeSystemInfo(time, ct);
        });
        
        return services;
    }
}