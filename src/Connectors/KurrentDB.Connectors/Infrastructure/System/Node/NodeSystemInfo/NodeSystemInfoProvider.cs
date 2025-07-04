// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using Kurrent.Surge;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Services;
using static System.Text.Json.JsonSerializer;

namespace KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;

public delegate ValueTask<NodeSystemInfo> GetNodeSystemInfo(CancellationToken cancellationToken = default);

public static class NodeSystemInfoProviderExtensions {
    public static async ValueTask<NodeSystemInfo> GetNodeSystemInfo(this IPublisher publisher, TimeProvider time, CancellationToken cancellationToken = default) =>
        await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken)
            .Then(re => Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!)
            .Then(evt => new NodeSystemInfo(evt.Members.Single(x => x.InstanceId == evt.NodeId), time.GetUtcNow()));

    static readonly JsonSerializerOptions GossipStreamSerializerOptions = new() {
        Converters = { new JsonStringEnumConverter() }
    };

    [UsedImplicitly]
    record GossipUpdatedInMemory(Guid NodeId, ClientClusterInfo.ClientMemberInfo[] Members);
}
