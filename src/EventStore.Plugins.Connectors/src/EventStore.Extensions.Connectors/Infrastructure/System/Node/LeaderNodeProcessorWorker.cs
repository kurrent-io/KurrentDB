using EventStore.Core.Bus;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class LeaderNodeProcessorWorker<T>(Func<T> getProcessor, IServiceProvider serviceProvider, string serviceName) :
    LeaderNodeBackgroundService(
        serviceProvider.GetRequiredService<IPublisher>(),
        serviceProvider.GetRequiredService<ISubscriber>(),
        serviceProvider.GetRequiredService<GetNodeSystemInfo>(),
        serviceProvider.GetRequiredService<ILoggerFactory>(),
        serviceName
    ) where T : IProcessor {
    protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        try {
            var processor = getProcessor();
            await processor.Activate(stoppingToken);
            await processor.Stopped;
        }
        catch (OperationCanceledException) {
            // ignored
        }
    }
}