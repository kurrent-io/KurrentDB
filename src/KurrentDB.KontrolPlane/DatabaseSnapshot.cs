// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Represents instant state of the database.
/// </summary>
public readonly record struct DatabaseSnapshot {
	/// <summary>
	/// Gets the database leader.
	/// </summary>
	/// <value><see langword="null"/> if the leader is not assigned.</value>
	public IDatabaseNode? Leader { get; init; }

	/// <summary>
	/// Gets the entire database.
	/// </summary>
	/// <value><see langword="null"/> if the database is removed.</value>
	public required IDatabase? Database { get; init; }
}
