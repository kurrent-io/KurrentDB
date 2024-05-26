using EventStore.Core.Cluster;
using EventStore.Core.Data;

namespace EventStore.Connectors.ControlPlane;

[PublicAPI]
public readonly record struct NodeInstanceInfo(ClientClusterInfo.ClientMemberInfo MemberInfo, DateTimeOffset Timestamp) {
	public Guid InstanceId  => MemberInfo.InstanceId;
	public bool IsLeader    => MemberInfo is { State: VNodeState.Leader, IsAlive: true };
	public bool IsNotLeader => !IsLeader;
}