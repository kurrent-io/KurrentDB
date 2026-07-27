// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

/// <summary>
/// Represents base class for Data Plane node host.
/// </summary>
public abstract partial class DatabaseNodeHost {
	/// <summary>
	/// Gets the replication state of the database node.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The state of this database node.</returns>
	protected internal abstract ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token);

	public Task StartAsync(CancellationToken token) => Task.CompletedTask;

	public Task StopAsync(CancellationToken token) => Task.CompletedTask;
}
