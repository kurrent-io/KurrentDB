// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;

namespace KurrentDB.Api.Infrastructure;

public interface INodeSystemInfoProvider {
    /// <summary>
    /// Gets information about the current node instance.
    /// </summary>
    ValueTask<NodeSystemInfo> GetInstanceInfo(CancellationToken cancellationToken);

    /// <summary>
    /// Gets information about the current cluster leader node.
    /// </summary>
    ValueTask<NodeSystemInfo> GetLeaderInfo(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if the current node is the cluster leader and returns leadership information.
    /// </summary>
    ValueTask<NodeLeadershipInfo> CheckLeadership(CancellationToken cancellationToken);
}

public sealed class NodeSystemInfoProvider(IPublisher publisher, TimeProvider time) : INodeSystemInfoProvider {
    static readonly JsonSerializerOptions GossipStreamSerializerOptions = new() {
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask<NodeSystemInfo> GetInstanceInfo(CancellationToken cancellationToken) {
        var re  = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken);
        var evt = JsonSerializer.Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!;
        return new(evt.Members.Single(node => node.InstanceId == evt.NodeId), time.GetUtcNow());
    }

    public async ValueTask<NodeSystemInfo> GetLeaderInfo(CancellationToken cancellationToken) {
        var re  = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken);
        var evt = JsonSerializer.Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!;
        return new(evt.Members.Single(node => node is { State: VNodeState.Leader, IsAlive: true }), time.GetUtcNow());
    }

    public async ValueTask<NodeLeadershipInfo> CheckLeadership(CancellationToken cancellationToken) {
        var re  = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken);
        var evt = JsonSerializer.Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!;

        // get instance info
        var info     = evt.Members.Single(node => node.InstanceId == evt.NodeId);
        var isleader = info is { State: VNodeState.Leader, IsAlive: true };

        // if not leader, return leader info
        if (!isleader)
            //TODO SS: Check if it possible that there is no leader when a request comes in. Is it handled on a higher level?
            info = evt.Members.SingleOrDefault(node => node is { State: VNodeState.Leader, IsAlive: true }) ?? info;

        return new(info.InstanceId, new(info.InternalTcpIp, info.InternalTcpPort), isleader);
    }

    [UsedImplicitly]
    record GossipUpdatedInMemory(Guid NodeId, ClientClusterInfo.ClientMemberInfo[] Members);
}

public readonly record struct NodeLeadershipInfo(Guid InstanceId, DnsEndPoint Endpoint, bool IsLeader) {
    public bool IsNotLeader => !IsLeader;
}
