// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace EventStore.TcpPlugin;

public class EventStoreOptions {
	public int ConnectionPendingSendBytesThreshold { get; init; } = 10 * 1_024 * 1_024;
	public int ConnectionQueueSizeThreshold { get; init; } = 50_000;
	public int WriteTimeoutMs { get; init; } = 2_000;
	public bool Insecure { get; init; }
	public IPAddress NodeIp { get; init; } = IPAddress.Loopback;
	public TcpPluginOptions TcpPlugin { get; init; } = new();

	public class TcpPluginOptions {
		public bool EnableExternalTcp { get; init; }
		public int NodeTcpPort { get; init; } = 1113;
		public int NodeHeartbeatInterval { get; init; } = 2_000;
		public int NodeHeartbeatTimeout { get; init; } = 1_000;
	}
}
