using EventStore.Connect.Connectors;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Control;

public class ControlPlaneCoordinatorService : LeadershipAwareService {
    public ControlPlaneCoordinatorService(
        INodeLifetimeService nodeLifetime, IConnectorFactory connectorFactory, TimeProvider timeProvider, ILoggerFactory loggerFactory
    ) : base(nodeLifetime, loggerFactory) { }

    protected override async Task Execute(CancellationToken stoppingToken) {
        ClusterNodeId nodeId = ClusterNodeId.None;
    }
}

public class LeadershipAwareProcessorHost : LeadershipAwareService {
    public LeadershipAwareProcessorHost(IProcessor processor, INodeLifetimeService nodeLifetime, ILoggerFactory loggerFactory) : base(
        nodeLifetime,
        loggerFactory
    ) {
        Processor = processor;
    }

    IProcessor Processor { get; }

    protected override async Task Execute(CancellationToken stoppingToken) {
        await Processor.Activate(stoppingToken);
    }
}