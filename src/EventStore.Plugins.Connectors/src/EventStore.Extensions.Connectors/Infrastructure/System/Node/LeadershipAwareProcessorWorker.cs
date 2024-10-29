using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class LeadershipAwareProcessorWorker<T>(T processor, IServiceProvider serviceProvider, string? name = null) :
    LeadershipAwareService(
        serviceProvider.GetRequiredService<GetNodeLifetimeService>(),
        serviceProvider.GetRequiredService<GetNodeSystemInfo>(),
        serviceProvider.GetRequiredService<ILoggerFactory>(),
        name
    ) where T : IProcessor {
    protected override Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) =>
        processor.RunUntilDeactivated(stoppingToken);
}