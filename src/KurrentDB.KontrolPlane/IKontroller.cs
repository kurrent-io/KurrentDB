// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

public interface IKontroller {
	IDatabaseReplicaSet ReplicaSet { get; init; }

	ValueTask<IReadOnlySet<string>> GetDatabasesAsync(CancellationToken token = default);

	ValueTask<DatabaseCluster?> GetDatabaseAsync(string databaseId, CancellationToken token = default);

	ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, CancellationToken token = default);

	ValueTask AddOrUpdateDatabaseAsync(Database database, CancellationToken token = default);

	ValueTask<bool> RemoveDatabaseAsync(string databaseId, CancellationToken token = default);

	ValueTask AddOrUpdateDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default);

	ValueTask<bool> RemoveDatabaseNodeAsync(string databaseId, EndPoint address, CancellationToken token = default);

	/// <summary>
	/// Listens for database changes.
	/// </summary>
	/// <param name="databaseId">The identifier of the database.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>A stream of full database snapshot. The stream finishes if <paramref name="databaseId"/> becomes deleted.</returns>
	IAsyncEnumerable<DatabaseCluster> ListenDatabaseAsync(string databaseId, CancellationToken token = default);
}
