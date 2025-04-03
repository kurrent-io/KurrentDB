// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;

namespace EventStore.Core.Data;

//WARNING: new states must be added at the bottom of the enum otherwise it may break cluster and client compatibility
public enum VNodeState {
	Initializing = 0,
	DiscoverLeader = 1,
	Unknown = 2,
	PreReplica = 3,
	CatchingUp = 4,
	Clone = 5,
	Follower = 6,
	PreLeader = 7,
	Leader = 8,
	Manager = 9,
	ShuttingDown = 10,
	Shutdown = 11,
	ReadOnlyLeaderless = 12,
	PreReadOnlyReplica = 13,
	ReadOnlyReplica = 14,
	ResigningLeader = 15,

	[EditorBrowsable(EditorBrowsableState.Never)]
	MaxValue = ResigningLeader,
}

public static class VNodeStateExtensions {
	public static bool IsReplica(this VNodeState state) {
		return state is VNodeState.CatchingUp or VNodeState.Clone or VNodeState.Follower
			or VNodeState.ReadOnlyReplica;
	}
}
