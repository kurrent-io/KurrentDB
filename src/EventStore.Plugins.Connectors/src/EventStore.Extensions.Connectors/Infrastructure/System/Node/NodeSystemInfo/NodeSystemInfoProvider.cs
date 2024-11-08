using System.Text.Json;
using System.Text.Json.Serialization;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Services;
using EventStore.Streaming;
using static System.Text.Json.JsonSerializer;

namespace EventStore.Connectors.System;

public delegate ValueTask<NodeSystemInfo> GetNodeSystemInfo(CancellationToken cancellationToken = default);

public interface INodeSystemInfoProvider {
    ValueTask<NodeSystemInfo> GetNodeSystemInfo(CancellationToken cancellationToken = default);
}

public sealed class NodeSystemInfoProvider(IPublisher publisher, TimeProvider time) : INodeSystemInfoProvider {
    static readonly JsonSerializerOptions GossipStreamSerializerOptions = new() {
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask<NodeSystemInfo> GetNodeSystemInfo(CancellationToken cancellationToken = default) =>
        await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, cancellationToken)
            .Then(re => Deserialize<GossipUpdatedInMemory>(re!.Value.Event.Data.Span, GossipStreamSerializerOptions)!)
            .Then(evt => new NodeSystemInfo(evt.Members.Single(x => x.InstanceId == evt.NodeId), time.GetUtcNow()));

    [UsedImplicitly]
    record GossipUpdatedInMemory(Guid NodeId, ClientClusterInfo.ClientMemberInfo[] Members);
}

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