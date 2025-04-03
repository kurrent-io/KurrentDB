// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Licensing.Keygen;

public record LicenseInfo {
	public record Conclusive(
		string LicenseId,
		string Name,
		bool Valid,
		bool Trial,
		bool Warning,
		string Detail,
		DateTimeOffset? Expiry,
		string[] Entitlements) : LicenseInfo {

		public static Conclusive FromError(string error) => new(
			LicenseId: "Unknown",
			Name: "Unknown",
			Valid: false,
			Trial: false,
			Warning: true,
			Detail: error,
			Expiry: null,
			Entitlements: []);
	}

	public record Inconclusive : LicenseInfo {
		public static Inconclusive Instance { get; } = new Inconclusive();
	}

	public record RetryImmediately : LicenseInfo {
		public static RetryImmediately Instance { get; } = new RetryImmediately();
	}
}

