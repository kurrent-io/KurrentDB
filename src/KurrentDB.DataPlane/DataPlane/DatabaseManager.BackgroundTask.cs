// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using KurrentDB.KontrolPlane;

namespace KurrentDB.DataPlane;

partial class DatabaseManager {
	private readonly AsyncStateTracker.Token _clusterInfoNullVersion;
	private readonly AsyncStateTracker _clusterInfoChanged;
	private Task _controlProcess;
	private volatile DatabaseCluster? _clusterInfo;

	private async Task CommunicateWithKontrolPlaneAsync() {
		await using var enumerator = KontrolPlane
			.AnnounceNodeAsync(DatabaseHandler.CurrentNode, _lifecycleToken)
			.GetAsyncEnumerator();

		if (!await enumerator.MoveNextAsync())
			return;

		var newVersion = enumerator.Current;

		_clusterInfo = newVersion;
		await ChangeStateAsync(baseline: null, newVersion);
		while (await enumerator.MoveNextAsync()) {
			newVersion = enumerator.Current;
			var oldVersion = _clusterInfo;

			// Ignore any information with stale epoch
			if (newVersion.Epoch >= oldVersion.Epoch) {
				_clusterInfo = newVersion;
				await ChangeStateAsync(oldVersion, newVersion);
			}
		}
	}
}
