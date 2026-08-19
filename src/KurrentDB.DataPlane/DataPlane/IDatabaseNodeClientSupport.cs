// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents functionality required to process client requests by the database node.
/// </summary>
public interface IDatabaseNodeClientSupport {
	/// <summary>
	/// Ensures that the current database node can process write requests.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The leadership token; or canceled token constructed with <see cref="CancellationToken(bool)"/>
	/// to indicate that the current node is not leader.</returns>
	ValueTask<CancellationToken> EnsureLeadershipAsync(CancellationToken token = default);

	/// <summary>
	/// Gets information about the hosted database.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The information about the database.</returns>
	ValueTask<DatabaseCluster> GetDatabaseInfoAsync(CancellationToken token = default);
}
