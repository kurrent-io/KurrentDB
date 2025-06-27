// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Settings;

namespace EventStore.TcpUnitTestPlugin;

public class TcpApiTestOptions {
	public int NodeTcpPort { get; init; } = 1113;
	public int NodeHeartbeatInterval { get; init; } = 2_000;
	public int NodeHeartbeatTimeout { get; init; } = 1_000;
	public int ConnectionPendingSendBytesThreshold { get; set; } = 10 * 1_024 * 1_024;
	public int ConnectionQueueSizeThreshold { get; set; } = 50_000;
	public int TcpReadTimeoutMs { get; set; } = ESConsts.ReadRequestTimeout;
	public int WriteTimeoutMs { get; set; } = 2_000;
	public bool Insecure { get; init; } = false;
}
