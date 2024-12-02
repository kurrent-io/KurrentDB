using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Toolkit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static System.Threading.Interlocked;

namespace EventStore.Connectors.System;

public delegate INodeLifetimeService GetNodeLifetimeService(string componentName);

public interface INodeLifetimeService {
    Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken);
}

[UsedImplicitly]
public sealed class NodeLifetimeService : IHandle<SystemMessage.StateChangeMessage>, INodeLifetimeService, IDisposable {
    volatile TokenCompletionSource?  _leadershipEvent = new();

    public NodeLifetimeService(string componentName, IPublisher publisher, ISubscriber subscriber, ILogger<NodeLifetimeService>? logger = null) {
        ComponentName = componentName;
        Publisher     = publisher;
        Subscriber    = subscriber;
        Logger        = logger ?? NullLoggerFactory.Instance.CreateLogger<NodeLifetimeService>();

        Subscriber.Subscribe(this);
    }

    ILogger     Logger        { get; }
    IPublisher  Publisher     { get; }
    ISubscriber Subscriber    { get; }
    string      ComponentName { get; }

    VNodeState CurrentState { get; set; } = VNodeState.Unknown;

    public void Handle(SystemMessage.StateChangeMessage message) {
        switch (_leadershipEvent) {
            case { Task.IsCompleted: false } when message.State is VNodeState.Leader:
                Logger.LogNodeStateChanged(ComponentName, CurrentState, CurrentState = message.State);
                _leadershipEvent.Complete();
                break;

            case { Task.IsCompleted: true } when message.State is not VNodeState.Leader:
                Logger.LogNodeStateChanged(ComponentName, CurrentState, CurrentState = message.State);
                using (var oldEvent = Exchange(ref _leadershipEvent, new())) oldEvent.Cancel();
                break;
        }
    }

    public async Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken) {
        if (_leadershipEvent is null)
            return new CancellationToken(canceled: true);

        if (cancellationToken.IsCancellationRequested)
            return cancellationToken;

        try {
            return await _leadershipEvent.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
            return cancellationToken;
        }
    }

    void Dispose(bool disposing) {
        if (!disposing)
            return;

        using var oldEvent = Exchange(ref _leadershipEvent, null);
        oldEvent?.Cancel();
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~NodeLifetimeService() => Dispose(false);
}

static partial class NodeLifetimeServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "{ComponentName} node state changed from {PreviousNodeState} to {NodeState}")]
    internal static partial void LogNodeStateChanged(this ILogger logger, string componentName, VNodeState previousNodeState, VNodeState nodeState);

    [LoggerMessage(LogLevel.Debug, "{ComponentName} node state ignored: {NodeState}")]
    internal static partial void LogNodeStateChangeIgnored(this ILogger logger, string componentName, VNodeState nodeState);
}