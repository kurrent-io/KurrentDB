// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

internal sealed class FollowerState(IDatabaseStateHandler handler, DatabaseCluster database) : DatabaseState {
	protected override Task RunAsync() {
		return database.LeaderNode is { } leaderNode
			? handler.RunReplicationAsync(database, leaderNode, Token)
			: Task.CompletedTask;
	}
}
