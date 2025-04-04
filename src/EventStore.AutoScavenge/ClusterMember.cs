// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;

namespace EventStore.AutoScavenge;

public class ClusterMember {
	public Guid InstanceId { get; set; } = default;
	public string State { get; set; } = string.Empty;
	public bool IsAlive { get; set; }
	public string InternalHttpEndPointIp { get; set; } = string.Empty;
	public int InternalHttpEndPointPort { get; set; }
	public long WriterCheckpoint { get; set; }
	public bool IsReadOnlyReplica { get; set; }
}

public static class ClusterMemberExtensions {
	public static NodeId ToNodeId(this ClusterMember m) =>
		new(m.InternalHttpEndPointIp, m.InternalHttpEndPointPort);
}
