using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class LeadershipAwareProcessorWorker<T> : LeadershipAwareService where T : IProcessor {
    protected LeadershipAwareProcessorWorker(T processor, IServiceProvider serviceProvider)
        : base(
            serviceProvider.GetRequiredService<Func<string, INodeLifetimeService>>(),
            serviceProvider.GetRequiredService<GetNodeSystemInfo>(),
            serviceProvider.GetRequiredService<ILoggerFactory>()
        ) => Processor = processor;

    IProcessor Processor { get; }

    protected override Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) =>
        Processor.RunUntilDeactivated(stoppingToken);
}