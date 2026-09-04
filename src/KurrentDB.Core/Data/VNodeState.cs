// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;

namespace KurrentDB.Core.Data;

// With KPlane the states and their semantics remain the same except that:
//   1. Unknown does not trigger elections. KPlane will appoint a leader.
//   2. DiscoverLeader is not used. KPlane will tell us who the leader is when it wants us to know.
//   3. ResigningLeader is not used. It could be in the future.
//
//WARNING: new states must be added at the bottom of the enum otherwise it may break cluster and client compatibility
public enum VNodeState {
	Initializing = 0,
	DiscoverLeader = 1,     // Check if there is already a leader according to gossip. If so become PreReplica, if not: start elections.
	Unknown = 2,            // Unknown who the leader is. Used to trigger an election but not under KPlane.
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

	// Not currently used in KPlane mode (it is not necessary for correcness),
	// but as further work, when Freezing as leader we could transition through this state.
	// The KPlane would need to fence the leader first and wait for a reply/timeout before fencing the others.
	ResigningLeader = 15,

	[EditorBrowsable(EditorBrowsableState.Never)]
	MaxValue = ResigningLeader,
}

public static class VNodeStateExtensions {
	public static bool IsReplica(this VNodeState state) {
		return state is VNodeState.CatchingUp or VNodeState.Clone or VNodeState.Follower
			or VNodeState.ReadOnlyReplica;
	}

	public static bool CanReplicateToOtherNodes(this VNodeState state) => state
		is VNodeState.PreLeader
		or VNodeState.Leader
		or VNodeState.ResigningLeader;

	// This is the same partition ReplicaService switches on.
	public static bool CanReplicateFromLeader(this VNodeState state) => state
		is VNodeState.PreReplica
		or VNodeState.PreReadOnlyReplica
		|| state.IsReplica();
}
