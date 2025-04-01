// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge;

public class EventStoreOptions {
	public bool Insecure { get; init; }
	public bool DiscoverViaDns { get; init; }
	public string? ClusterDns { get; init; }
	public int ClusterSize { get; init; }
}
