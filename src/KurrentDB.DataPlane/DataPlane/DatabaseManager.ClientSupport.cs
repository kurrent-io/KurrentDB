// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

partial class DatabaseManager {
	public ValueTask<DatabaseCluster> GetDatabaseInfoAsync(CancellationToken token = default)
		=> _clusterInfo is { } clusterInfo ? ValueTask.FromResult(clusterInfo) : WaitForClusterInfoAsync(token);

	public CancellationToken LeadershipToken
		=> (Volatile.Read(in _state) as LeaderState)?.Token ?? new(canceled: true);

	private async ValueTask<DatabaseCluster> WaitForClusterInfoAsync(CancellationToken token = default) {
		await _clusterInfoChanged.WaitNextAsync(_clusterInfoNullVersion, token);
		var clusterInfo = _clusterInfo;
		ObjectDisposedException.ThrowIf(clusterInfo is null, this);
		return clusterInfo;
	}
}
