using EventStore.Core.Bus;
using EventStore.Core.Messages;

namespace EventStore.Connectors.System;

public interface ISystemReadinessProbe {
    ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken);
}

public class SystemReadinessProbe : MessageModule, ISystemReadinessProbe {
    public SystemReadinessProbe(ISubscriber subscriber, GetNodeSystemInfo getNodeSystemInfo) : base(subscriber) {
        On<SystemMessage.BecomeLeader>((_, token) => Ready(token));
        On<SystemMessage.BecomeFollower>((_, token) => Ready(token));
        On<SystemMessage.BecomeReadOnlyReplica>((_, token) => Ready(token));

        return;

        ValueTask Ready(CancellationToken token) =>
            Sensor.Signal(() => {
                DropAll();
                return getNodeSystemInfo();
            }, token);
    }

    SystemSensor<NodeSystemInfo> Sensor { get; }  = new();

    public ValueTask<NodeSystemInfo> WaitUntilReady(CancellationToken cancellationToken) =>
        Sensor.WaitForSignal(cancellationToken);
}

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