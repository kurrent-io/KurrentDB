// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using static System.Threading.Interlocked;

namespace KurrentDB.SchemaRegistry.Infrastructure.System.Node;

public delegate INodeLifetimeService GetNodeLifetimeService(string componentName);

public interface INodeLifetimeService {
    Task<CancellationToken> WaitForLeadershipAsync(CancellationToken cancellationToken);
    Task WaitForSystemReadyAsync(CancellationToken cancellationToken);
}

[UsedImplicitly]
public sealed class NodeLifetimeService : IHandle<SystemMessage.StateChangeMessage>, IHandle<SystemMessage.SystemReady>, INodeLifetimeService, IDisposable {
    volatile TokenCompletionSource?  _leadershipEvent = new();
    readonly TaskCompletionSource _systemReadyEvent = new();

    public NodeLifetimeService(string componentName, IPublisher publisher, ISubscriber subscriber, ILogger<NodeLifetimeService>? logger = null) {
        ComponentName = componentName;
        Publisher     = publisher;
        Subscriber    = subscriber;
        Logger        = logger ?? NullLoggerFactory.Instance.CreateLogger<NodeLifetimeService>();

        Subscriber.Subscribe<SystemMessage.StateChangeMessage>(this);
        Subscriber.Subscribe<SystemMessage.SystemReady>(this);
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

    public void Handle(SystemMessage.SystemReady message) {
        Logger.LogSystemReady(ComponentName);
        _systemReadyEvent.TrySetResult();
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

    public async Task WaitForSystemReadyAsync(CancellationToken cancellationToken) {
        try {
            await _systemReadyEvent.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
	        // ignore
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

    [LoggerMessage(LogLevel.Debug, "{ComponentName} system ready")]
    internal static partial void LogSystemReady(this ILogger logger, string componentName);
}
