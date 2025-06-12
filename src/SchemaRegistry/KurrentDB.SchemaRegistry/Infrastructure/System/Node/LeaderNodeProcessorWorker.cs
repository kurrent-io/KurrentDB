// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Processors;
using KurrentDB.SchemaRegistry.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SchemaRegistry.Infrastructure.System.Node;

public abstract class LeaderNodeProcessorWorker<T>(Func<T> getProcessor, IServiceProvider serviceProvider, string serviceName) :
    LeaderNodeBackgroundService(
        serviceProvider.GetRequiredService<IPublisher>(),
        serviceProvider.GetRequiredService<ISubscriber>(),
        serviceProvider.GetRequiredService<GetNodeSystemInfo>(),
        serviceProvider.GetRequiredService<ILoggerFactory>(),
        serviceName
    ) where T : IProcessor {
    protected override async Task Execute(NodeSystemInfo.NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
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
