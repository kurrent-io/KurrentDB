// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Represents instant state of the database.
/// </summary>
public sealed record DatabaseCluster : Database {
	public IReadOnlyList<DatabaseNode> Nodes {
		get;
		init;
	} = [];

	public EndPoint? LeaderAddress { get; init; }

	public TimeSpan LeaderAppointmentDuration { get; init; }

	public DatabaseNode? LeaderNode => Nodes.FirstOrDefault(LeaderAddress.IsAddressEqual);

	public DatabaseNode? this[EndPoint address] => Nodes.FirstOrDefault(address.IsAddressEqual);
}

file static class DatabaseNodeCollectionExtensions {
	public static bool IsAddressEqual(this EndPoint? address, DatabaseNode databaseNode)
		=> databaseNode.Address.Equals(address);
}
