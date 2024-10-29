using DotNext.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class LeadershipAwareService : BackgroundService {
    protected LeadershipAwareService(GetNodeLifetimeService getNodeLifetimeService, GetNodeSystemInfo getNodeSystemInfo, ILoggerFactory loggerFactory, string? name = null) {
        NodeLifetime      = getNodeLifetimeService(name ?? GetType().Name);
        GetNodeSystemInfo = getNodeSystemInfo;
        Logger            = loggerFactory.CreateLogger(name ?? GetType().Name);
    }

    INodeLifetimeService NodeLifetime      { get; }
    GetNodeSystemInfo    GetNodeSystemInfo { get; }

    protected ILogger Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (stoppingToken.IsCancellationRequested) return;

        Logger.LogServiceStarted();

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

        Logger.LogServiceStopped();
        NodeLifetime.ReportComponentTerminated();
    }

    protected abstract Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}

static partial class LeadershipAwareServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "Service host started")]
    internal static partial void LogServiceStarted(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Service host stopped")]
    internal static partial void LogServiceStopped(this ILogger logger);
}