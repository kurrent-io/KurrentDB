// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core;

// Still needed for gossip and stats.
public class NodeTcpOptions {
	public int NodeTcpPort { get; init; } = 1113;
	public bool EnableExternalTcp { get; init; }
	public int? NodeTcpPortAdvertiseAs { get; init; }
}
