using EventStore.Core.Bus;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors.System;

public static class NodeSystemInfoWireUpExtensions {
    public static IServiceCollection AddNodeSystemInfoProvider(this IServiceCollection services) =>
        services.AddSingleton<GetNodeSystemInfo>(ctx => {
            var publisher = ctx.GetRequiredService<IPublisher>();
            var time      = ctx.GetRequiredService<TimeProvider>();
            return token => publisher.GetNodeSystemInfo(time, token);
        });
}