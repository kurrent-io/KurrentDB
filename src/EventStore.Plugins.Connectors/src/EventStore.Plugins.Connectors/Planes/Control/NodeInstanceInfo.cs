using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Data;

namespace EventStore.Connectors.Control;

[PublicAPI]
public readonly record struct NodeInstanceInfo {
    public NodeInstanceInfo(ClientClusterInfo.ClientMemberInfo memberInfo, DateTimeOffset timestamp) {
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

//
// [PublicAPI]
// public readonly record struct NodeInstanceInfo {
//     public NodeInstanceInfo(ClientClusterInfo.ClientMemberInfo memberInfo, DateTimeOffset timestamp) {
//         MemberInfo = memberInfo;
//         Timestamp  = timestamp;
//
//         InstanceId  = MemberInfo.InstanceId;
//         IsLeader    = MemberInfo is { State: VNodeState.Leader, IsAlive: true };
//         IsNotLeader = !IsLeader;
//
//         InternalTcpEndPoint = new DnsEndPoint(MemberInfo.InternalTcpIp, MemberInfo.InternalTcpPort);
//
//         InternalSecureTcpEndPoint = MemberInfo.InternalSecureTcpPort > 0
//             ? new DnsEndPoint(MemberInfo.InternalTcpIp, MemberInfo.InternalSecureTcpPort)
//             : null;
//
//         ExternalTcpEndPoint = MemberInfo.ExternalTcpIp != null ? new DnsEndPoint(MemberInfo.ExternalTcpIp, MemberInfo.ExternalTcpPort) : null;
//
//         ExternalSecureTcpEndPoint = MemberInfo is { ExternalTcpIp: not null, ExternalSecureTcpPort: > 0 }
//             ? new DnsEndPoint(MemberInfo.ExternalTcpIp, MemberInfo.ExternalSecureTcpPort)
//             : null;
//
//         HttpEndPoint = new DnsEndPoint(MemberInfo.HttpEndPointIp, MemberInfo.HttpEndPointPort);
//     }
//
//     public ClientClusterInfo.ClientMemberInfo MemberInfo { get; init; }
//     public DateTimeOffset                     Timestamp  { get; init; }
//
//     public Guid InstanceId  { get; init; } //=> MemberInfo.InstanceId;
//     public bool IsLeader    { get; init; } //=> MemberInfo is { State: VNodeState.Leader, IsAlive: true };
//     public bool IsNotLeader { get; init; } //=> !IsLeader;
//
//     public DnsEndPoint  InternalTcpEndPoint       { get; init; }
//     public DnsEndPoint? InternalSecureTcpEndPoint { get; init; }
//     public DnsEndPoint? ExternalTcpEndPoint       { get; init; }
//     public DnsEndPoint? ExternalSecureTcpEndPoint { get; init; }
//     public DnsEndPoint  HttpEndPoint              { get; init; }
//
//     // public DnsEndPoint InternalTcpEndPoint => new DnsEndPoint(MemberInfo.InternalTcpIp, MemberInfo.InternalTcpPort);
//     //
//     // public DnsEndPoint? InternalSecureTcpEndPoint => MemberInfo.InternalSecureTcpPort > 0
//     //     ? new DnsEndPoint(MemberInfo.InternalTcpIp, MemberInfo.InternalSecureTcpPort)
//     //     : null;
//     //
//     // public DnsEndPoint? ExternalTcpEndPoint => MemberInfo.ExternalTcpIp != null ? new DnsEndPoint(MemberInfo.ExternalTcpIp, MemberInfo.ExternalTcpPort) : null;
//     //
//     // public DnsEndPoint? ExternalSecureTcpEndPoint => MemberInfo is { ExternalTcpIp: not null, ExternalSecureTcpPort: > 0 }
//     //     ? new DnsEndPoint(MemberInfo.ExternalTcpIp, MemberInfo.ExternalSecureTcpPort)
//     //     : null;
//     //
//     // public DnsEndPoint HttpEndPoint => new DnsEndPoint(MemberInfo.HttpEndPointIp, MemberInfo.HttpEndPointPort);
// }
