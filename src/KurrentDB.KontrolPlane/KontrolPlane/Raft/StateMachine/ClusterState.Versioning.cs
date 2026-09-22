// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

partial class ClusterState {
	private const int LatestVersion = 0;

	private static SortedDictionary<int, Action<DuckDBAdvancedConnection>> MigrationActions
		=> new();
}
