using EventStore.Connectors.Control;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;


public class ClusterTopologyProbe : MessageModule {
    public ClusterTopologyProbe(ISubscriber subscriber, GetNodeSystemInfo getNodeSystemInfo, ILogger<ClusterTopologyProbe> logger) : base(subscriber) {
        var systemReady = false;

        On<SystemMessage.BecomeLeader>((_, _) => Ready());
        On<SystemMessage.BecomeFollower>((_, _) => Ready());
        On<SystemMessage.BecomeReadOnlyReplica>((_, _) => Ready());

        var actualTopology = ClusterTopology.Unknown;

        On<GossipMessage.GossipUpdated>(async (evt, token) => {
            if (!systemReady)
                return;

            var updatedTopology = ClusterTopology.From(MapToClusterNodes(evt.ClusterInfo.Members));

            var nodeInfo = await getNodeSystemInfo();

            await Sensor.Signal(async () => {
                return (nodeInfo, updatedTopology);
            }, token);

            actualTopology = updatedTopology;

            logger.LogDebug(
                "[{NodeId}|{NodeState}] Cluster Topology Change Detected: {GossipMembersInfo}",
                nodeInfo.InstanceId, nodeInfo.MemberInfo.State, evt.ClusterInfo.Members.Select(x => (x.State, x.InstanceId))
            );
        });

        return;

        void Ready() {
            DropAll();
            systemReady = true;
        }
    }

    SystemSensor<(NodeSystemInfo, ClusterTopology)> Sensor { get; }  = new();

    public ValueTask<(NodeSystemInfo, ClusterTopology)> WaitForChanges(CancellationToken cancellationToken) =>
        Sensor.WaitForSignal(cancellationToken);

    static ClusterNode[] MapToClusterNodes(MemberInfo[] members) =>
        members
            .Where(x => x.IsAlive)
            .Select(x => new ClusterNode {
                NodeId = x.InstanceId,
                State  = MapToClusterNodeState(x.State)
            })
            .ToArray();

    static ClusterNodeState MapToClusterNodeState(VNodeState state) =>
        state switch {
            VNodeState.Leader          => ClusterNodeState.Leader,
            VNodeState.Follower        => ClusterNodeState.Follower,
            VNodeState.ReadOnlyReplica => ClusterNodeState.ReadOnlyReplica,
            _                          => ClusterNodeState.Unmapped
        };
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