// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Represents instant state of the database.
/// </summary>
public sealed class DatabaseCluster : Database {
	public IReadOnlyList<DatabaseNode> Nodes {
		get => field ?? [];
		init;
	}

	public EndPoint? LeaderAddress { get; init; }
}
