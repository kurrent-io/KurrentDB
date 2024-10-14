using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors.System;

public static class NodeSystemInfoWireUpExtensions {
    public static IServiceCollection AddNodeSystemInfoProvider(this IServiceCollection services) =>
        services
            .AddSingleton<INodeSystemInfoProvider, NodeSystemInfoProvider>()
            .AddSingleton<GetNodeSystemInfo>(ctx => {
                var provider = ctx.GetRequiredService<INodeSystemInfoProvider>();
                return token => provider.GetNodeSystemInfo(token);
            });
}