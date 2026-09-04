// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Represents Kontroller node metadata.
/// </summary>
public readonly record struct KontrollerMetadata {
	/// <summary>
	/// Gets or sets Kontroller API port number.
	/// </summary>
	public required int ApiPort { get; init; }
}
