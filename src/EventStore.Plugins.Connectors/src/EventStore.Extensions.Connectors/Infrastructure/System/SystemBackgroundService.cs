using Microsoft.Extensions.Hosting;

namespace EventStore.Connectors.System;

public abstract class SystemBackgroundService(SystemReadinessProbe probe) : BackgroundService {
    SystemReadinessProbe ReadinessProbe { get; } = probe;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var nodeInfo = await ReadinessProbe.WaitUntilReady(stoppingToken);
        await Execute(nodeInfo, stoppingToken);
    }

    protected abstract Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}