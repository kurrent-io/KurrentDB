// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Serialization;

namespace KurrentDB.Core.Services.Storage.InMemory;

public class GossipListenerService : IHandle<GossipMessage.GossipUpdated> {
	private readonly Guid _nodeId;
	public const string EventType = "$GossipUpdated";

	public SingleEventInMemoryStream Stream { get; }

	public GossipListenerService(Guid nodeId, IPublisher publisher, InMemoryLog memLog) {
		Stream = new(publisher, memLog, SystemStreams.GossipStream);
		_nodeId = nodeId;
	}

	public void Handle(GossipMessage.GossipUpdated message) {
		// SystemStreams.GossipStream is a system stream so only readable by admins
		// we use ClientMemberInfo because plugins will consume this stream and
		// it is less likely to change than the internal gossip.
		var payload = new GossipUpdatedPayload(
			NodeId: _nodeId,
			Members: message.ClusterInfo.Members.Select(static x =>
				new ClientClusterInfo.ClientMemberInfo(x)));

		var data = JsonSerializer.SerializeToUtf8Bytes(payload, CoreJsonContext.Default.GossipUpdatedPayload);
		Stream.Write(EventType, data);
	}

	internal sealed record GossipUpdatedPayload(Guid NodeId, IEnumerable<ClientClusterInfo.ClientMemberInfo> Members);
}
