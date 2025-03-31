using DotNext.Threading;
using EventStore.Core.Bus;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

public abstract class LeaderNodeBackgroundService : NodeBackgroundService {
    protected LeaderNodeBackgroundService(
        IPublisher publisher,
        ISubscriber subscriber,
        GetNodeSystemInfo getNodeSystemInfo,
        ILoggerFactory loggerFactory,
        string? serviceName = null
    ) : base(publisher, loggerFactory.CreateLogger<NodeBackgroundService>(), serviceName) {
        // GetNodeLifetimeService = component => new NodeLifetimeService(
        //     component, publisher, subscriber,
        //     loggerFactory.CreateLogger<NodeLifetimeService>()
        // );

        NodeLifetimeService = new NodeLifetimeService(
            ServiceName, publisher, subscriber,
            loggerFactory.CreateLogger<NodeLifetimeService>()
        );

        GetNodeSystemInfo = getNodeSystemInfo;

        Logger = loggerFactory.CreateLogger<LeaderNodeBackgroundService>();
    }

    // GetNodeLifetimeService GetNodeLifetimeService { get; }
    INodeLifetimeService   NodeLifetimeService    { get; }
    GetNodeSystemInfo      GetNodeSystemInfo      { get; }

    protected ILogger Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        stoppingToken.Register(() => Logger.LogLeaderNodeBackgroundServiceShuttingDown(ServiceName));

        if (stoppingToken.IsCancellationRequested) return;

        // INodeLifetimeService nodeLifetime = GetNodeLifetimeService(ServiceName);

        while (!stoppingToken.IsCancellationRequested) {
            var lifetimeToken = await NodeLifetimeService.WaitForLeadershipAsync(stoppingToken);

            if (lifetimeToken.IsCancellationRequested)
                break;

            Logger.LogLeaderNodeBackgroundServiceLeadershipAssigned(ServiceName);

            var token       = lifetimeToken;
            var cancellator = token.LinkTo(stoppingToken);

            try {
                var nodeInfo = await GetNodeSystemInfo(stoppingToken);

                // it only runs on a leader node, so if the cancellation
                // token is canceled, it means the node lost leadership
                await Execute(nodeInfo, cancellator!.Token);

                if (cancellator.CancellationOrigin != stoppingToken)
                    Logger.LogLeaderNodeBackgroundServiceLeadershipRevoked(ServiceName);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) {
                Logger.LogLeaderNodeBackgroundServiceError(ex, ServiceName, ex.Message);
                break;
            }
            finally {
                cancellator?.Dispose();
            }
        }

        Logger.LogLeaderNodeBackgroundServiceStopped(ServiceName);
    }

    protected abstract Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}

static partial class LeaderNodeBackgroundServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "{ServiceName} node leadership assigned, running...")]
    internal static partial void LogLeaderNodeBackgroundServiceLeadershipAssigned(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Debug, "{ServiceName} node leadership revoked, stopping...")]
    internal static partial void LogLeaderNodeBackgroundServiceLeadershipRevoked(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Debug, "{ServiceName} node shutting down, stopping...")]
    internal static partial void LogLeaderNodeBackgroundServiceShuttingDown(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Debug, "{ServiceName} stopped")]
    internal static partial void LogLeaderNodeBackgroundServiceStopped(this ILogger logger, string serviceName);

    [LoggerMessage(LogLevel.Critical, "{ServiceName} error detected: {ErrorMessage}")]
    internal static partial void LogLeaderNodeBackgroundServiceError(this ILogger logger, Exception error, string serviceName, string errorMessage);
}