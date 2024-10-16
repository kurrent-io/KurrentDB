using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
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
                switch (message) {
                    case SystemMessage.BecomeShuttingDown shuttingDown: {
                        if (shuttingDown.ShutdownHttp)
                            Logger.LogNodeShuttingDownByEndpointRequest();
                        else
                            Logger.LogNodeShuttingDownByExitProcessRequest();

                        break;
                    }

                    default: Logger.LogNodeLeadershipRevoked(message.State); break;
                }

                using (var oldEvent = Interlocked.Exchange(ref _leadershipEvent, new()))
                    oldEvent.Cancel(null);

                break;

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
    [LoggerMessage(LogLevel.Debug, "Node state changed from {OldState} to {NewState}")]
    internal static partial void LogNodeStateChanged(this ILogger logger, VNodeState oldState, VNodeState newState);

    [LoggerMessage(LogLevel.Debug, "Node leadership assigned")]
    internal static partial void LogNodeLeadershipAssigned(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Node leadership revoked: {NodeState}")]
    internal static partial void LogNodeLeadershipRevoked(this ILogger logger, VNodeState nodeState);

    [LoggerMessage(LogLevel.Debug, "Node shutting down by endpoint request!")]
    internal static partial void LogNodeShuttingDownByEndpointRequest(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Node shutting down by process request!")]
    internal static partial void LogNodeShuttingDownByExitProcessRequest(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Node violently shutting down!")]
    internal static partial void LogNodeShuttingDown(this ILogger logger);
}