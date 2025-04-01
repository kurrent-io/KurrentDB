// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge.Clients;

public abstract class GossipAwareBase : IGossipAware {
	private readonly object _lock = new();
	private NodeEndpoint? _leaderNode;
	private bool _isLeader;

	public void ReceiveGossipMessage(GossipMessage msg) {
		lock (_lock) {
			_leaderNode = null;
			_isLeader = false;
			foreach (var member in msg.Members) {
				if (member.State == "Leader") {
					_leaderNode = new NodeEndpoint {
						Host = member.InternalHttpEndPointIp,
						Port = member.InternalHttpEndPointPort,
					};

					_isLeader = member.InstanceId == msg.NodeId;
					return;
				}
			}
		}
	}

	public bool TryGetCurrentLeader(out NodeEndpoint leader, out bool isSelf) {
		lock (_lock) {
			isSelf = _isLeader;
			leader = _leaderNode ?? default;
			return _leaderNode is not null;
		}
	}
}
