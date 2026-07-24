// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.DataPlane;

/// <summary>
/// Manages communication with the member in the Data Plane.
/// </summary>
public interface IDataPlane : IAsyncDisposable {
	/// <summary>
	/// Gets the replication state for the specified member.
	/// </summary>
	/// <param name="address">The address of the member.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The replication state of the member.</returns>
	ValueTask<ReplicaState> GetReplicaStateAsync(EndPoint address, CancellationToken token);

	/// <summary>
	/// Instructs the client to keep the specified connections alive.
	/// </summary>
	/// <param name="activeConnections">A set of connections to be retained alive.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns></returns>
	ValueTask ReclaimConnectionsAsync(IReadOnlySet<EndPoint> activeConnections, CancellationToken token)
		=> ValueTask.CompletedTask;
}
