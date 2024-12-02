using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Streaming;
using EventStore.Toolkit;

namespace EventStore.Connectors.System;

public interface ISystemReadinessProbe {
    ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken);
}

[UsedImplicitly]
public class SystemReadinessProbe : IHandle<SystemMessage.BecomeLeader>, IHandle<SystemMessage.BecomeFollower>, IHandle<SystemMessage.BecomeReadOnlyReplica> {
    public SystemReadinessProbe(ISubscriber subscriber, GetNodeSystemInfo getNodeSystemInfo) {
        CompletionSource = new();

        Subscriber = subscriber.With(x => {
            x.Subscribe<SystemMessage.BecomeLeader>(this);
            x.Subscribe<SystemMessage.BecomeFollower>(this);
            x.Subscribe<SystemMessage.BecomeReadOnlyReplica>(this);
        });

        GetNodeSystemInfo = getNodeSystemInfo;
    }

    ISubscriber          Subscriber        { get; }
    GetNodeSystemInfo    GetNodeSystemInfo { get; }
    TaskCompletionSource CompletionSource  { get; }

    public void Handle(SystemMessage.BecomeLeader message)          => CompletionSource.TrySetResult();
    public void Handle(SystemMessage.BecomeFollower message)        => CompletionSource.TrySetResult();
    public void Handle(SystemMessage.BecomeReadOnlyReplica message) => CompletionSource.TrySetResult();

    public async ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken = default) {
        await CompletionSource.Task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
        Subscriber.Unsubscribe<SystemMessage.BecomeLeader>(this);
        Subscriber.Unsubscribe<SystemMessage.BecomeFollower>(this);
        Subscriber.Unsubscribe<SystemMessage.BecomeReadOnlyReplica>(this);
        return await GetNodeSystemInfo();
    }
}

//
// public class SystemReadinessProbe : MessageModule, ISystemReadinessProbe {
//     public SystemReadinessProbe(ISubscriber subscriber, GetNodeSystemInfo getNodeSystemInfo) : base(subscriber) {
//         On<SystemMessage.BecomeLeader>((_, token) => Ready(token));
//         On<SystemMessage.BecomeFollower>((_, token) => Ready(token));
//         On<SystemMessage.BecomeReadOnlyReplica>((_, token) => Ready(token));
//
//         return;
//
//         ValueTask Ready(CancellationToken token) =>
//             Sensor.Signal(() => {
//                 DropAll();
//                 return getNodeSystemInfo();
//             }, token);
//     }
//
//     SystemSensor<NodeSystemInfo> Sensor { get; }  = new();
//
//     public ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken) =>
//         Sensor.WaitForSignal(cancellationToken);
// }

// public class SystemReadinessProbe : MessageModule {
//     public SystemReadinessProbe(ISubscriber subscriber, GetNodeSystemInfo getNodeSystemInfo, ILogger<SystemReadinessProbe> logger) : base(subscriber) {
//         CompletionSource = new();
//
//         On<SystemMessage.BecomeLeader>((_, token) => Ready(token));
//         On<SystemMessage.BecomeFollower>((_, token) => Ready(token));
//         On<SystemMessage.BecomeReadOnlyReplica>((_, token) => Ready(token));
//
//         return;
//
//         async ValueTask Ready(CancellationToken token) {
//             DropAll();
//
//             var info = await getNodeSystemInfo();
//
//             logger.LogDebug("System Ready >> {NodeInfo}", new { NodeId = info.InstanceId, State = info.MemberInfo.State });
//
//             if (!token.IsCancellationRequested)
//                 CompletionSource.TrySetResult(await getNodeSystemInfo());
//             else
//                 CompletionSource.TrySetCanceled(token);
//         }
//     }
//
//     TaskCompletionSource<NodeSystemInfo> CompletionSource { get; }
//
//     public Task<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken = default) =>
//         CompletionSource.Task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
// }