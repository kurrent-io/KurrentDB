// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Services;
using KurrentDB.SchemaRegistry.Infrastructure;
using static System.Text.Json.JsonSerializer;

namespace KurrentDB.SchemaRegistry.Infrastructure.System.Node.NodeSystemInfo;

public delegate ValueTask<NodeSystemInfo> GetNodeSystemInfo(CancellationToken cancellationToken = default);

public static class NodeSystemInfoProviderExtensions {
    public static async ValueTask<NodeSystemInfo> GetNodeSystemInfo(this IPublisher publisher, TimeProvider time, CancellationToken cancellationToken = default) =>
        await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken)
            .Then(re => Deserialize(re!.Value.Event.Data.Span, SchemaRegistryJsonContext.Default.GossipUpdatedInMemory)!)
            .Then(evt => new NodeSystemInfo(evt.Members.Single(x => x.InstanceId == evt.NodeId), time.GetUtcNow()));

    [UsedImplicitly]
    internal record GossipUpdatedInMemory(Guid NodeId, ClientClusterInfo.ClientMemberInfo[] Members);
}
