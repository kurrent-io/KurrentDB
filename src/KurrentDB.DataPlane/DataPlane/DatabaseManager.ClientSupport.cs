// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

partial class DatabaseManager : IDatabaseNodeClientSupport {
	public ValueTask<DatabaseCluster> GetDatabaseInfoAsync(CancellationToken token = default)
		=> _clusterInfo is { } clusterInfo ? ValueTask.FromResult(clusterInfo) : WaitForClusterInfoAsync(token);

	public ValueTask<CancellationToken> EnsureLeadershipAsync(CancellationToken token = default)
		=> Volatile.Read(in _state).WaitForWriteBarrierAsync(token);

	private async ValueTask<DatabaseCluster> WaitForClusterInfoAsync(CancellationToken token = default) {
		await _clusterInfoChanged.WaitNextAsync(_clusterInfoNullVersion, token);
		var clusterInfo = _clusterInfo;
		ObjectDisposedException.ThrowIf(clusterInfo is null, this);
		return clusterInfo;
	}
}
