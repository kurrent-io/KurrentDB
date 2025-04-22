using System.Net;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Data;

namespace EventStore.Connectors.System;

[PublicAPI]
public readonly record struct NodeSystemInfo {
    public NodeSystemInfo(ClientClusterInfo.ClientMemberInfo memberInfo, DateTimeOffset timestamp) {
        MemberInfo = memberInfo;
        Timestamp  = timestamp;

        InstanceId  = MemberInfo.InstanceId;
        IsLeader    = MemberInfo is { State: VNodeState.Leader, IsAlive: true };
        IsNotLeader = !IsLeader;

        InternalTcpEndPoint = new DnsEndPoint(MemberInfo.InternalTcpIp, MemberInfo.InternalTcpPort);
        HttpEndPoint        = new DnsEndPoint(MemberInfo.HttpEndPointIp, MemberInfo.HttpEndPointPort);
    }

    public ClientClusterInfo.ClientMemberInfo MemberInfo { get; }
    public DateTimeOffset                     Timestamp  { get; }

    public Guid InstanceId  { get; }
    public bool IsLeader    { get; }
    public bool IsNotLeader { get; }

    public DnsEndPoint InternalTcpEndPoint { get; }
    public DnsEndPoint HttpEndPoint        { get; }
}
