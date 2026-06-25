// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Represents database leader.
/// </summary>
public sealed class DatabaseLeader : KontrollerEntity {
	public required DatabaseNode Node { get; init; }

	public required ulong Epoch { get; init; }
}
