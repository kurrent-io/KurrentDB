// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Data;

public class VNodeStateExtensionsTests {
	// Every state, and what each predicate says about it. Exhaustive on purpose: adding a VNodeState
	// without classifying it here fails all_states_are_classified below, which is the point. The
	// Kontrol Plane's fence depends on the answer - a state wrongly reported as not replicating would
	// be answered as frozen without the node having stopped.
	private static readonly Dictionary<VNodeState, (bool IsReplica, bool ToOtherNodes, bool FromLeader)> Expected = new() {
		[VNodeState.Initializing] = (false, false, false),
		[VNodeState.DiscoverLeader] = (false, false, false),
		[VNodeState.Unknown] = (false, false, false),
		[VNodeState.PreReplica] = (false, false, true),
		[VNodeState.CatchingUp] = (true, false, true),
		[VNodeState.Clone] = (true, false, true),
		[VNodeState.Follower] = (true, false, true),
		[VNodeState.PreLeader] = (false, true, false),
		[VNodeState.Leader] = (false, true, false),
		[VNodeState.Manager] = (false, false, false),
		[VNodeState.ShuttingDown] = (false, false, false),
		[VNodeState.Shutdown] = (false, false, false),
		[VNodeState.ReadOnlyLeaderless] = (false, false, false),
		[VNodeState.PreReadOnlyReplica] = (false, false, true),
		[VNodeState.ReadOnlyReplica] = (true, false, true),
		[VNodeState.ResigningLeader] = (false, true, false),
	};

	public static TheoryData<VNodeState> States {
		get {
			var data = new TheoryData<VNodeState>();
			foreach (var state in Expected.Keys)
				data.Add(state);

			return data;
		}
	}

	[Theory]
	[MemberData(nameof(States))]
	public void classifies_each_state(VNodeState state) {
		var expected = Expected[state];

		Assert.Equal(expected.IsReplica, state.IsReplica());
		Assert.Equal(expected.ToOtherNodes, state.CanReplicateToOtherNodes());
		Assert.Equal(expected.FromLeader, state.CanReplicateFromLeader());
	}

	[Fact]
	public void all_states_are_classified() {
		// MaxValue aliases ResigningLeader, hence Distinct.
		var declared = Enum.GetValues<VNodeState>().Distinct().ToHashSet();

		Assert.Equal(declared, Expected.Keys.ToHashSet());
	}

	[Theory]
	[MemberData(nameof(States))]
	public void no_state_replicates_in_both_directions(VNodeState state) {
		// The leadership edge in ClusterVNodeController's State setter watches only
		// CanReplicateToOtherNodes. A state that was both would end leadership while the node was
		// still receiving replication.
		Assert.False(state.CanReplicateToOtherNodes() && state.CanReplicateFromLeader());
	}

	[Theory]
	[MemberData(nameof(States))]
	public void replicas_receive_replication(VNodeState state) {
		// IsReplica is the subset of CanReplicateFromLeader that has finished subscribing, so it
		// cannot contain a state that does not replicate from the leader at all.
		if (state.IsReplica())
			Assert.True(state.CanReplicateFromLeader());
	}
}
