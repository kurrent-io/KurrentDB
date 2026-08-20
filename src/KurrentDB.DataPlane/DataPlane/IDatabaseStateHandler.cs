// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// An interface between database and DPlane logic.
/// </summary>
public interface IDatabaseStateHandler {
	/// <summary>
	/// Runs replication of the data from the specified leader node.
	/// </summary>
	/// <remarks>
	/// This method implements Follower state logic.
	/// </remarks>
	/// <param name="database">The database descriptor.</param>
	/// <param name="leaderNode">The appointed leader.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The task representing asynchronous state of the operation.</returns>
	Task RunReplicationAsync(Database database, DatabaseNode leaderNode, CancellationToken token);

	/// <summary>
	/// Runs leadership.
	/// </summary>
	/// <remarks>
	/// This method implements Leader state logic.
	/// </remarks>
	/// <param name="changes">An infinite sequence of database cluster changes.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The task representing asynchronous state of the operation.</returns>
	Task RunLeadershipAsync(IAsyncEnumerable<DatabaseCluster> changes, CancellationToken token);

	/// <summary>
	/// Gets the replication state
	/// </summary>
	///  <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The replication state for the current node.</returns>
	ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token);

	/// <summary>
	/// Gets or sets the current node.
	/// </summary>
	DatabaseNode CurrentNode { get; set; }
}
