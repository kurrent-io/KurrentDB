// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge;

public class GossipMessage {
	public Guid NodeId { get; init; } = default;
	public List<ClusterMember> Members { get; init; } = new();
}
