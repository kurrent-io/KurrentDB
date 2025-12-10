// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public static class CustomIndexConstants {
	public const string Category = $"${nameof(CustomIndex)}";
	public const string ManagementStream = $"$idx-ce-{Category}";
	public const char FieldDelimiter = ':';
}
