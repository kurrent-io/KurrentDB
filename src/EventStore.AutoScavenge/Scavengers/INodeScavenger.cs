// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge.Scavengers;

public interface INodeScavenger {
	/// <returns>Returns a scavenge id if it was successful, null otherwise</returns>
	Task<Guid?> TryStartScavengeAsync(string host, int port, CancellationToken token);

	Task<ScavengeStatus> TryGetScavengeAsync(string host, int port, Guid scavengeId, CancellationToken token);

	Task<bool> TryPauseScavengeAsync(string host, int port, Guid? scavengeId, CancellationToken token);
}
