// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using static System.Text.Json.JsonSerializer;

namespace KurrentDB;

[PublicAPI]
public readonly record struct NodeSystemInfo {
	public static readonly NodeSystemInfo Empty = new(null!, DateTimeOffset.MinValue);

    public NodeSystemInfo(ClientClusterInfo.ClientMemberInfo memberInfo, DateTimeOffset timestamp) {
        MemberInfo = memberInfo;
        Timestamp  = timestamp;

        InstanceId  = MemberInfo.InstanceId;
        IsLeader    = MemberInfo is { State: VNodeState.Leader, IsAlive: true };
        IsNotLeader = !IsLeader;

        InternalTcpEndPoint = new(MemberInfo.InternalTcpIp, MemberInfo.InternalTcpPort);
        HttpEndPoint        = new(MemberInfo.HttpEndPointIp, MemberInfo.HttpEndPointPort);
    }

    public ClientClusterInfo.ClientMemberInfo MemberInfo { get; }
    public DateTimeOffset                     Timestamp  { get; }

    public Guid InstanceId  { get; }
    public bool IsLeader    { get; }
    public bool IsNotLeader { get; }

    public DnsEndPoint InternalTcpEndPoint { get; }
    public DnsEndPoint HttpEndPoint        { get; }
}

public readonly record struct LeadershipInfo(Guid InstanceId, DnsEndPoint Endpoint, bool IsLeader) {
	public bool IsNotLeader => !IsLeader;
}

public sealed class NodeSystemInfoProvider(IPublisher publisher, TimeProvider time) {
	public async ValueTask<NodeSystemInfo> GetInstanceInfo(CancellationToken cancellationToken = default) {
		var re = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken);
		var evt = Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!;
		return new(evt.Members.Single(node => node.InstanceId == evt.NodeId), time.GetUtcNow());
	}

	public async ValueTask<NodeSystemInfo> GetLeaderInfo(CancellationToken cancellationToken = default) {
		var re = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken);
		var evt = Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!;
		return new(evt.Members.Single(node => node is { State: VNodeState.Leader, IsAlive: true }), time.GetUtcNow());
	}

	public async ValueTask<LeadershipInfo> CheckLeadership(CancellationToken cancellationToken = default) {
		var re = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken);
		var evt = Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!;

		// get instance info
		var info     = evt.Members.Single(node => node.InstanceId == evt.NodeId);
		var isleader = info is { State: VNodeState.Leader, IsAlive: true };

		// if not leader, return leader info
		if (!isleader)
			//TODO SS: Check if it possible that there is no leader when a request comes in. Is it handled on a higher level?
			info = evt.Members.SingleOrDefault(node => node is { State: VNodeState.Leader, IsAlive: true }) ?? info;

		return new(info.InstanceId, new(info.InternalTcpIp, info.InternalTcpPort), isleader);
	}

	static readonly JsonSerializerOptions GossipStreamSerializerOptions = new() {
		Converters = { new JsonStringEnumConverter() }
	};

	[UsedImplicitly]
	record GossipUpdatedInMemory(Guid NodeId, ClientClusterInfo.ClientMemberInfo[] Members);
}

public static class NodeSystemInfoWireUpExtensions {
	public static IServiceCollection AddNodeSystemInfoProvider(this IServiceCollection services) =>
		services.AddSingleton<NodeSystemInfoProvider>();
}
