// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents database node.
/// </summary>
public interface IDatabaseNode {
	/// <summary>
	/// Represents a token that remains non-canceled for a whole period of the current node leadership.
	/// </summary>
	/// <remarks>
	/// If <see cref="CancellationToken.IsCancellationRequested"/> returns <see langword="false"/> then
	/// the current node is a database leader.
	/// </remarks>
	CancellationToken LeadershipToken { get; }

	/// <summary>
	/// Enumerates database leadership changes.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>An infinite sequence of the database leaders.</returns>
	IAsyncEnumerable<LeaderAppointment> GetDatabaseLeadersAsync(CancellationToken token = default);

	/// <summary>
	/// Gets information about the hosted database.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The information about the database.</returns>
	ValueTask<DatabaseCluster> GetDatabaseInfoAsync(CancellationToken token = default);

	/// <summary>
	/// Enumerates all changes to the database cluster membership list.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>A sequence which returns a list of database nodes every time when a node is added, removed, or modified.
	/// The caller is responsible to compute the diff more precisely if needed.</returns>
	IAsyncEnumerable<IReadOnlySet<DatabaseNode>> GetDatabaseMembershipChangesAsync(CancellationToken token = default);

	/// <summary>
	/// Gets information about the current node.
	/// </summary>
	DatabaseNode CurrentNode { get; }
}
