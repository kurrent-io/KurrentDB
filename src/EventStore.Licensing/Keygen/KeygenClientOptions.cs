// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Licensing.Keygen;

public record KeygenClientOptions {
	public int NodePort { get; set; }

	public bool Archiver { get; set; }

	public bool ReadOnlyReplica { get; set; }

	public LicensingOptions Licensing { get; set; } = new();

	public record LicensingOptions {
		public string LicenseKey { get; set; } = "";

		public string? BaseUrl { get; set; }

		public bool IncludePortInFingerprint { get; set; }
	}
}
