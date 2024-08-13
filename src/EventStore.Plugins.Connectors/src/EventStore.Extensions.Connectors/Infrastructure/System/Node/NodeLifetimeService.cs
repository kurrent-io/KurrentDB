using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.System;

public interface INodeLifetimeService {
    Task<CancellationToken> WaitForLeadershipAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

[UsedImplicitly]
public sealed class NodeLifetimeService : IHandle<SystemMessage.StateChangeMessage>, INodeLifetimeService, IDisposable {
    volatile TokenCompletionSource? _leadershipEvent = new();

    public NodeLifetimeService(ISubscriber? subscriber = null, ILogger<NodeLifetimeService>? logger = null) {
        subscriber?.Subscribe(this);
        Logger = logger ?? NullLoggerFactory.Instance.CreateLogger<NodeLifetimeService>();
    }

    ILogger Logger { get; }

    public void Handle(SystemMessage.StateChangeMessage message) {
        switch (_leadershipEvent) {
            case { Task.IsCompleted: false } when message.State is VNodeState.Leader:
                Logger.LogNodeLeadershipAssigned();
                _leadershipEvent.Complete();

                break;

            case { Task.IsCompleted: true } when message.State is not VNodeState.Leader:
                if (message is SystemMessage.BecomeShuttingDown shuttingDown) {
                    if (shuttingDown.ShutdownHttp)
                        Logger.LogNodeShuttingDownByEndpointRequest();
                    else
                        Logger.LogNodeShuttingDownByExitProcessRequest();
                }
                else
                    Logger.LogNodeLeadershipRevoked(message.State);

                using (var oldEvent = Interlocked.Exchange(ref _leadershipEvent, new()))
                    oldEvent.Cancel(null);

                break;

            // ESDB does not support graceful shutdown, so at this point,
            // if we cancel the token, the connectors will just throw a lot of errors...
            // default:
            //     if (message.State is VNodeState.ShuttingDown) {
            //         Logger.LogNodeShuttingDown();
            //         using var oldEvent = Interlocked.Exchange(ref _leadershipEvent, null);
            //         oldEvent?.Cancel(new ReadResponseException.NotHandled.ServerNotReady());
            //         oldEvent?.Dispose();
            //     }
            //
            //     break;

            default:
                if (message.State is VNodeState.ShuttingDown) {
                    Logger.LogNodeShuttingDown();
                    using var oldEvent = Interlocked.Exchange(ref _leadershipEvent, null);
                    oldEvent?.Cancel(null);
                }

                break;
        }
    }

    public Task<CancellationToken> WaitForLeadershipAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _leadershipEvent?.Task.WaitAsync(timeout, cancellationToken) ?? Task.FromException<CancellationToken>(new ObjectDisposedException(GetType().Name));

    void Dispose(bool disposing) {
        if (!disposing)
            return;

        using var oldEvent = Interlocked.Exchange(ref _leadershipEvent, null);
        oldEvent?.Cancel(null);
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~NodeLifetimeService() => Dispose(false);
}

static partial class NodeLifetimeServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "Node leadership assigned")]
    internal static partial void LogNodeLeadershipAssigned(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Node leadership revoked: {NodeState}")]
    internal static partial void LogNodeLeadershipRevoked(this ILogger logger, VNodeState nodeState);

    [LoggerMessage(LogLevel.Debug, "Node shutting down by endpoint request!")]
    internal static partial void LogNodeShuttingDownByEndpointRequest(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Node shutting down by exit process request!")]
    internal static partial void LogNodeShuttingDownByExitProcessRequest(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Node violently shutting down!")]
    internal static partial void LogNodeShuttingDown(this ILogger logger);
}