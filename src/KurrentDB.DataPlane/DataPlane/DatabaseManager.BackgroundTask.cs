// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using KurrentDB.KontrolPlane;

namespace KurrentDB.DataPlane;

partial class DatabaseManager {
	private readonly AsyncStateTracker.Token _clusterInfoNullVersion;
	private readonly AsyncStateTracker _clusterInfoChanged;
	private Task _controlProcess;
	private DatabaseCluster? _clusterInfo;

	private async Task CommunicateWithKontrolPlaneAsync() {
		await foreach (var clusterInfo in KontrolPlane.AnnounceNodeAsync(DatabaseHandler.CurrentNode, _lifecycleToken)) {
			await ChangeStateAsync(clusterInfo);
		}
	}
}
