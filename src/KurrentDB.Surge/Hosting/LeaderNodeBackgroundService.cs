// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Surge.Hosting;

public abstract class LeaderNodeBackgroundService : NodeBackgroundService {
    private readonly CancellationTokenMultiplexer _multiplexer;

    protected LeaderNodeBackgroundService(
        IPublisher publisher,
        ISubscriber subscriber,
        GetNodeSystemInfo getNodeSystemInfo,
        ILoggerFactory loggerFactory,
        string? serviceName = null
    ) : base(publisher, loggerFactory.CreateLogger<NodeBackgroundService>(), serviceName) {
        NodeLifetimeService = new NodeLifetimeService(
            ServiceName, publisher, subscriber,
            loggerFactory.CreateLogger<NodeLifetimeService>()
        );

        GetNodeSystemInfo = getNodeSystemInfo;

        Logger = loggerFactory.CreateLogger<LeaderNodeBackgroundService>();

        _multiplexer = new() { MaximumRetained = 10 };
    }

    INodeLifetimeService NodeLifetimeService { get; }
    GetNodeSystemInfo    GetNodeSystemInfo   { get; }

    protected ILogger Logger { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        stoppingToken.Register(() => Logger.LogLeaderNodeBackgroundServiceShuttingDown(ServiceName));

        if (stoppingToken.IsCancellationRequested) return;

        while (!stoppingToken.IsCancellationRequested) {
            var lifetimeToken = await NodeLifetimeService.WaitForLeadershipAsync(stoppingToken);

            if (lifetimeToken.IsCancellationRequested)
                break;

            Logger.LogLeaderNodeBackgroundServiceLeadershipAssigned(ServiceName);

            var cancellator = _multiplexer.Combine([lifetimeToken, stoppingToken]);
            try {
                var nodeInfo = await GetNodeSystemInfo(stoppingToken);

                // it only runs on a leader node, so if the cancellation
                // token is canceled, it means the node lost leadership
                await Execute(nodeInfo, cancellator.Token);

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
                await cancellator.DisposeAsync();
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
