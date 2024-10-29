using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static System.Threading.Interlocked;

namespace EventStore.Connectors.System;

public delegate INodeLifetimeService GetNodeLifetimeService(string componentName);

public interface INodeLifetimeService {
    Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken);
    void                    ReportComponentTerminated();
}

[UsedImplicitly]
public sealed class NodeLifetimeService : IHandle<SystemMessage.StateChangeMessage>, INodeLifetimeService, IDisposable {
    volatile TokenCompletionSource?  _leadershipEvent = new();

    public NodeLifetimeService(string componentName, IPublisher publisher, ISubscriber subscriber, ILogger<NodeLifetimeService>? logger = null) {
        ComponentName = componentName;
        Publisher     = publisher;
        Logger        = logger ?? NullLoggerFactory.Instance.CreateLogger<NodeLifetimeService>();

        subscriber.Subscribe(this);

        Publisher.Publish(new SystemMessage.RegisterForGracefulTermination(
            ComponentName, () => {
                Logger.LogNodeLeadershipRevoked(ComponentName, VNodeState.ShuttingDown);
                using (var oldEvent = Exchange(ref _leadershipEvent, null)) oldEvent?.Cancel(null);
                subscriber.Unsubscribe(this);
            })
        );
    }

    ILogger    Logger        { get; }
    IPublisher Publisher     { get; }
    string     ComponentName { get; }

    public void Handle(SystemMessage.StateChangeMessage message) {
        switch (_leadershipEvent) {
            case { Task.IsCompleted: false } when message.State is VNodeState.Leader:
                Logger.LogNodeLeadershipAssigned(ComponentName);
                _leadershipEvent.Complete();
                break;

            case { Task.IsCompleted: true } when message.State is not VNodeState.Leader:
                Logger.LogNodeLeadershipRevoked(ComponentName, message.State);
                using (var oldEvent = Exchange(ref _leadershipEvent, new())) oldEvent.Cancel(null);
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

    public void ReportComponentTerminated() =>
        Publisher.Publish(new SystemMessage.ComponentTerminated(ComponentName));

    void Dispose(bool disposing) {
        if (!disposing)
            return;

        using var oldEvent = Exchange(ref _leadershipEvent, null);
        oldEvent?.Cancel(null);
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~NodeLifetimeService() => Dispose(false);
}

static partial class NodeLifetimeServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "{ComponentName} node leadership assigned")]
    internal static partial void LogNodeLeadershipAssigned(this ILogger logger, string componentName);

    [LoggerMessage(LogLevel.Debug, "{ComponentName} node leadership revoked: {NodeState}")]
    internal static partial void LogNodeLeadershipRevoked(this ILogger logger, string componentName, VNodeState nodeState);
}