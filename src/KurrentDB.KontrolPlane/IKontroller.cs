// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

public interface IKontroller {
	/// <summary>
	/// Gets a list of the databases registered in the Kontrol Plane.
	/// </summary>
	IReadOnlyDictionary<string, IDatabase> Databases { get; }

	IDatabaseReplicaSet ReplicaSet { get; init; }

	ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, CancellationToken token = default);

	ValueTask<bool> AddOrUpdateDatabaseAsync(string databaseId, string description = "", CancellationToken token = default);

	ValueTask<bool> RemoveDatabaseAsync(string databaseId, CancellationToken token = default);

	ValueTask<bool> AddOrUpdateDatabaseNodeAsync(string databaseId, IDatabaseNode node, CancellationToken token = default);

	ValueTask<bool> RemoveDatabaseNodeAsync(string databaseId, EndPoint address, CancellationToken token = default);

	IAsyncEnumerable<DatabaseSnapshot> ListenDatabaseAsync(string databaseId, CancellationToken token = default);
}
