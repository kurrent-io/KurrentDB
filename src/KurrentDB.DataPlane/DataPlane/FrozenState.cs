// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).


namespace KurrentDB.DataPlane;

using KontrolPlane;

internal class FrozenState : DatabaseState {
	protected override Task RunAsync() => Task.CompletedTask;
}

internal sealed class ResigningState(IKontrolPlane kontrolPlane, string databaseId, ulong currentEpoch) : FrozenState {
	protected override Task RunAsync()
		=> kontrolPlane.ResignLeaderAsync(databaseId, currentEpoch, Token);
}
