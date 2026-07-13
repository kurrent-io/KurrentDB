// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Describes database node.
/// </summary>
public sealed record DatabaseNode : IEntity {
	public required string DatabaseId { get; init; }

	public required EndPoint Address { get; init; }

	public DatabaseNodeRole Role { get; init; }
}
