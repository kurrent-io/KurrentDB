// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane;

/// <summary>
/// Describes Kurrent database.
/// </summary>
public record Database : IModelEntity {
	public const string MainDatabaseId = "main";

	public required string Id { get; init; }

	public ulong Epoch { get; init; }

	public string Description {
		get;
		init;
	} = string.Empty;
}
