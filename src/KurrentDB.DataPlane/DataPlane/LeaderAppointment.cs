// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents leader appointment visible on the Data Plane side.
/// </summary>
public sealed record LeaderAppointment {
	public required DatabaseNode Leader { get; init; }
	public required ulong Epoch { get; init; }
}
