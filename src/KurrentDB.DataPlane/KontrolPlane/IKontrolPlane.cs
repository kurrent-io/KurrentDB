// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

/// <summary>
/// An interface to access the Kontrol Plane from the Data Plane.
/// </summary>
public interface IKontrolPlane {
	/// <summary>
	/// Announces the database node and enumerates database cluster changes.
	/// </summary>
	/// <param name="node">The database node to be announced to the Kontrol Plane.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>An infinite sequence of database changes. The only way to interrupt it to cancel <paramref name="token"/>.</returns>
	IAsyncEnumerable<DatabaseCluster> AnnounceNodeAsync(DatabaseNode node, CancellationToken token = default);

	/// <summary>
	/// Updates the leader appointment.
	/// </summary>
	/// <param name="databaseId">The database identifier.</param>
	/// <param name="nodeAddress">The address of the caller database node.</param>
	/// <param name="nodeEpoch">The epoch of the caller database node.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns><see langword="true"/> if leader appointment is updated successfully; otherwise, <see langword="false"/>.</returns>
	ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint nodeAddress, ulong nodeEpoch, CancellationToken token = default);
}
