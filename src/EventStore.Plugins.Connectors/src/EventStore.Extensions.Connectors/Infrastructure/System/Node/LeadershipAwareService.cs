using DotNext.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class LeadershipAwareService : BackgroundService {
    protected LeadershipAwareService(GetNodeLifetimeService getNodeLifetimeService, GetNodeSystemInfo getNodeSystemInfo, ILoggerFactory loggerFactory, string serviceName) {
        NodeLifetime      = getNodeLifetimeService(serviceName);
        GetNodeSystemInfo = getNodeSystemInfo;
        ServiceName       = serviceName;
        Logger            = loggerFactory.CreateLogger<LeadershipAwareService>();
    }

    INodeLifetimeService NodeLifetime      { get; }
    GetNodeSystemInfo    GetNodeSystemInfo { get; }
    string               ServiceName       { get; }

    protected ILogger Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (stoppingToken.IsCancellationRequested) return;

        Logger.LogServiceStarted(ServiceName);

        while (!stoppingToken.IsCancellationRequested) {
            var lifetimeToken = await NodeLifetime.WaitForLeadershipAsync(stoppingToken);

            if (lifetimeToken.IsCancellationRequested)
                break;

            var token       = lifetimeToken;
            var cancellator = token.LinkTo(stoppingToken);

            try {
                var nodeInfo = await GetNodeSystemInfo(stoppingToken);

                // it only runs on a leader node, so if the cancellation
                // token is cancelled, it means the node lost leadership
                await Execute(nodeInfo, cancellator!.Token);
            }
            catch (OperationCanceledException) {
                break;
            }
            finally {
                cancellator?.Dispose();
            }
        }

        Logger.LogServiceStopped(ServiceName);
        NodeLifetime.ReportComponentTerminated();
    }

    protected abstract Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}

static partial class LeadershipAwareServiceLogMessages {
    [LoggerMessage(LogLevel.Trace, "{ServiceName} host started")]
    internal static partial void LogServiceStarted(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Trace, "{ServiceName} host stopped")]
    internal static partial void LogServiceStopped(this ILogger logger, string serviceName);
}