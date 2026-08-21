// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;

namespace KurrentDB.Core.Data;

//WARNING: new states must be added at the bottom of the enum otherwise it may break cluster and client compatibility
public enum VNodeState {
	Initializing = 0,
	DiscoverLeader = 1,     // Checking if there is already a leader. If so become PreReplica, if not: start elections.
	Unknown = 2,            // Triggers an election (it is Unknown who the leader is)
	PreReplica = 3,         // Elections done, we are in the quorum but not the leader elect.
	CatchingUp = 4,         // PreReplica -> CatchingUp when we are subscribed to the leader
	Clone = 5,              // CatchingUp -> Clone. Assigned this role by leader after we have caught up.
	Follower = 6,           // Follower. Assigned this role by leader.
	PreLeader = 7,          // Elected leader but not leader yet.
	Leader = 8,
	Manager = 9,             // Defunct
	ShuttingDown = 10,
	Shutdown = 11,
	// Leader does not assign the RoR specific states
	ReadOnlyLeaderless = 12, // RoR when leader is unknown
	PreReadOnlyReplica = 13, // RoR when not yet subscribed to replication or lost subscription
	ReadOnlyReplica = 14,    // RoR when subscribed. Equivalent of the Catching Up -> Clone -> Follower process.
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
