using EventStore.Connectors.Control;
using EventStore.Connectors.System;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Management.Reactors;

public class LeadershipAwareProcessorWorker<T> : LeadershipAwareService where T : IProcessor {
    public LeadershipAwareProcessorWorker(T processor, IServiceProvider serviceProvider)
        : base(
            serviceProvider.GetRequiredService<INodeLifetimeService>(),
            serviceProvider.GetRequiredService<GetNodeSystemInfo>(),
            serviceProvider.GetRequiredService<ILoggerFactory>()
        ) => Processor = processor;

    IProcessor Processor { get; }

    protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) =>
        await Processor.RunUntilDeactivated(stoppingToken);
}