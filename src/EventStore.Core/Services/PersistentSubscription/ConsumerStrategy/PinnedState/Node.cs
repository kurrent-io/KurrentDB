// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy.PinnedState;

internal class Node {
	internal enum NodeState {
		Connected,
		Disconnected
	}

	public Node() {
	}

	public Node(Guid connectionId, Guid nodeId, string host, int port, NodeState state,
		PersistentSubscriptionClient client, int maximumInFlightMessages, int assignmentCount) {
		ConnectionId = connectionId;
		NodeId = nodeId;
		Host = host;
		Port = port;
		State = state;
		Client = client;
		MaximumInFlightMessages = maximumInFlightMessages;
		AssignmentCount = assignmentCount;
	}

	public Node(PersistentSubscriptionClient client) {
		Client = client;
		ConnectionId = client.ConnectionId;
		NodeId = client.CorrelationId;

		var portSplit = client.From.IndexOf(':');
		if (portSplit == -1) {
			Host = client.From;
		} else {
			Host = client.From.Substring(0, portSplit);
			Port = Int32.Parse(client.From.Substring(portSplit + 1));
		}

		State = NodeState.Connected;
		MaximumInFlightMessages = client.MaximumInFlightMessages;
	}

	public Guid ConnectionId { get; set; }

	public Guid NodeId { get; set; }

	public string Host { get; set; }

	public int Port { get; set; }

	public NodeState State { get; set; }

	public PersistentSubscriptionClient Client { get; set; }

	public int MaximumInFlightMessages { get; set; }

	public int AssignmentCount { get; set; }
}
