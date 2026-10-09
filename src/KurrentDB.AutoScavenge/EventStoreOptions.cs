// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.AutoScavenge;

public class EventStoreOptions {
	public bool Insecure { get; set; }
	public bool DisableTls { get; set; }
	public bool TlsDisabled() => Insecure || DisableTls;
	public bool DiscoverViaDns { get; set; }
	public string? ClusterDns { get; set; }
	public int ClusterSize { get; set; }
}
