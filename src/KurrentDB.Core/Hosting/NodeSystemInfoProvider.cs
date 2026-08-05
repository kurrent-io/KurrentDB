// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Services;
using static System.Text.Json.JsonSerializer;

namespace KurrentDB.Core.Hosting;

public static class NodeSystemInfoProvider {
	public static async ValueTask<NodeSystemInfo> GetNodeSystemInfo(this IPublisher publisher, TimeProvider time, CancellationToken ct = default) {
		var lastEvent = await publisher.ReadStreamLastEvent(SystemStreams.GossipStream, ct);
		var gossip    = Deserialize<GossipUpdatedInMemory>(lastEvent!.Value.Event.Data.Span, GossipStreamSerializerOptions)!;
		return new(gossip.Members.Single(x => x.InstanceId == gossip.NodeId), time.GetUtcNow());
	}

	static readonly JsonSerializerOptions GossipStreamSerializerOptions = new() {
		Converters = { new JsonStringEnumConverter() }
	};

	[UsedImplicitly]
	record GossipUpdatedInMemory(Guid NodeId, ClientClusterInfo.ClientMemberInfo[] Members);
}

public delegate ValueTask<NodeSystemInfo> GetNodeSystemInfo(CancellationToken ct = default);