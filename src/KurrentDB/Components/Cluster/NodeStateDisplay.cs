// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.Components.Cluster;

// Maps a node's cluster role to a short label for display in the UI.
public static class NodeStateDisplay {
	public static string Label(VNodeState state) => state switch {
		VNodeState.Leader => "Leader",
		VNodeState.Follower => "Follower",
		VNodeState.ReadOnlyReplica => "Read-only replica",
		VNodeState.ReadOnlyLeaderless => "Read-only (no leader)",
		VNodeState.PreReadOnlyReplica => "Read-only replica (joining)",
		VNodeState.Clone => "Clone",
		VNodeState.CatchingUp => "Catching up",
		VNodeState.PreReplica => "Replica (joining)",
		VNodeState.PreLeader => "Leader (assuming)",
		VNodeState.ResigningLeader => "Resigning leader",
		VNodeState.DiscoverLeader => "Discovering leader",
		VNodeState.ShuttingDown => "Shutting down",
		VNodeState.Shutdown => "Shut down",
		VNodeState.Manager => "Manager",
		_ => state.ToString(),
	};
}
