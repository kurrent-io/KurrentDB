using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class LeadershipAwareProcessorWorker<T>(Func<T> getProcessor, IServiceProvider serviceProvider, string? name = null) :
    LeadershipAwareService(
        serviceProvider.GetRequiredService<GetNodeLifetimeService>(),
        serviceProvider.GetRequiredService<GetNodeSystemInfo>(),
        serviceProvider.GetRequiredService<ILoggerFactory>(),
        name
    ) where T : IProcessor {
    protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        try {
            var processor = getProcessor();
            await processor.Activate(stoppingToken);
            await processor.Stopped;
            // // THIS
            // var wtf = processor.Stopped;
            // await wtf;
        }
        catch (OperationCanceledException) {
            // ignored
        }
    }
}