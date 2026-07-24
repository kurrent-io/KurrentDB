// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

public interface IKontroller {
	ValueTask<IReadOnlySet<string>> GetDatabasesAsync(CancellationToken token = default);

	ValueTask<DatabaseCluster?> GetDatabaseAsync(string databaseId, CancellationToken token = default);

	ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, ulong epoch, CancellationToken token = default);

	ValueTask AddOrUpdateDatabaseAsync(Database database, CancellationToken token = default);

	ValueTask<bool> RemoveDatabaseAsync(string databaseId, CancellationToken token = default);

	ValueTask AddOrUpdateDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default);

	ValueTask<bool> TryAddDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default);

	ValueTask<bool> RemoveDatabaseNodeAsync(string databaseId, EndPoint address, CancellationToken token = default);

	/// <summary>
	/// Gets database leader appointment duration.
	/// </summary>
	TimeSpan AppointmentDuration { get; }

	/// <summary>
	/// Listens for database changes.
	/// </summary>
	/// <param name="databaseId">The identifier of the database.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>A stream of full database snapshot. The stream finishes if <paramref name="databaseId"/> becomes deleted.</returns>
	IAsyncEnumerable<DatabaseCluster> ListenDatabaseAsync(string databaseId, CancellationToken token = default);

	/// <summary>
	/// Gets a token associated with leader state of the current instance of the Kontroller.
	/// </summary>
	CancellationToken LeadershipToken { get; }

	/// <summary>
	/// Ensures that the current Kontroller instance is a part of the KPlane cluster and the cluster leader is observable.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The address of the KPlane leader.</returns>
	ValueTask<EndPoint> WaitForLeaderAsync(CancellationToken token = default);
}
