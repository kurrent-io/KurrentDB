// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Surge.Testing;

public static class Identifiers {
	public static string GenerateShortId(string? prefix = null) {
		var id = Guid.NewGuid().ToString("N")[26..];
		return prefix is not null ? $"{prefix}-{id}" : id;
	}

	public static string GenerateLongId(string? prefix = null) {
		var id = Guid.NewGuid().ToString("N");
		return prefix is not null ? $"{prefix}-{id}" : id;
	}
}
