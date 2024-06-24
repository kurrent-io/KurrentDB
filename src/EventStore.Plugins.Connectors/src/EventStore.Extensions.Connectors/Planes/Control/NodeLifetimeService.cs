using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.Control;

public interface INodeLifetimeService {
    Task<CancellationToken> WaitForLeadershipAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

[UsedImplicitly]
public sealed class NodeLifetimeService : IHandle<SystemMessage.StateChangeMessage>, INodeLifetimeService, IDisposable {
    volatile LeadershipCompletionSource? _leadershipEvent = new();

    public NodeLifetimeService(ISubscriber? subscriber = null, ILogger<NodeLifetimeService>? logger = null) {
        subscriber?.Subscribe(this);
        Logger = logger ?? NullLoggerFactory.Instance.CreateLogger<NodeLifetimeService>();
    }

    ILogger Logger { get; }

    public void Handle(SystemMessage.StateChangeMessage message) {
        switch (_leadershipEvent) {
            case { Task.IsCompleted: false } when message.State is VNodeState.Leader:
                _leadershipEvent.Assigned();
                Logger.LogNodeLeadershipAssigned();
                break;

            case { Task.IsCompleted: true } when message.State is not VNodeState.Leader:
                using (var oldEvent = Interlocked.Exchange(ref _leadershipEvent, new()))
                    oldEvent.Revoked();

                Logger.LogNodeLeadershipRevoked(message.State);

                break;

            default:
                if (message.State is VNodeState.ShuttingDown) {
                    using var oldEvent = Interlocked.Exchange(ref _leadershipEvent, null);
                    oldEvent?.Revoked();
                    Logger.LogNodeLeadershipRevoked(message.State);
                }

                break;
        }
    }

    public Task<CancellationToken> WaitForLeadershipAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _leadershipEvent?.Task.WaitAsync(timeout, cancellationToken) ?? Task.FromException<CancellationToken>(new ObjectDisposedException(GetType().Name));

    sealed class LeadershipCompletionSource : TaskCompletionSource<CancellationToken>, IDisposable {
        readonly CancellationTokenSource _cts;
        readonly CancellationToken       _token; // cached to avoid ObjectDisposedException

        public LeadershipCompletionSource() : base(TaskCreationOptions.RunContinuationsAsynchronously) {
            _cts   = new();
            _token = _cts.Token;
        }

        public void Assigned() => TrySetResult(_token);

        public void Revoked() => _cts.Cancel();

        void Dispose(bool disposing) {
            if (!disposing)
                return;

            TrySetException(new ObjectDisposedException(GetType().Name));
            _cts.Dispose();
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~LeadershipCompletionSource() => Dispose(false);
    }

    void Dispose(bool disposing) {
        if (!disposing)
            return;

        using (var oldEvent = Interlocked.Exchange(ref _leadershipEvent, null)) {
            oldEvent?.Revoked();
        }
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~NodeLifetimeService() {
        Dispose(false);
    }
}

static partial class NodeLifetimeServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "Node leadership assigned")]
    internal static partial void LogNodeLeadershipAssigned(this ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Node leadership revoked: {NodeState}")]
    internal static partial void LogNodeLeadershipRevoked(this ILogger logger, VNodeState nodeState);
}