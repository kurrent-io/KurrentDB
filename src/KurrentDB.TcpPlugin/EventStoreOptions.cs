// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using KurrentDB.Core;
using KurrentDB.Core.Settings;

namespace KurrentDB.TcpPlugin;

public class EventStoreOptions {
	private IPAddress _nodeIp = IPAddress.Loopback;

	public int ConnectionPendingSendBytesThreshold { get; set; } = 10 * 1_024 * 1_024;
	public int ConnectionQueueSizeThreshold { get; set; } = 50_000;
	public int WriteTimeoutMs { get; set; } = 2_000;
	public bool Insecure { get; set; }
	public bool DisableTls { get; set; }
	public bool TlsDisabled() => Insecure || DisableTls;

	public string NodeIp {
		get => _nodeIp.ToString();
		set => _nodeIp = IPAddress.Parse(value);
	}

	internal IPAddress GetNodeIp() => _nodeIp;

	public TcpPluginOptions TcpPlugin { get; set; } = new();

	public class TcpPluginOptions : NodeTcpOptions {
		public int NodeHeartbeatInterval { get; set; } = 2_000;
		public int NodeHeartbeatTimeout { get; set; } = 1_000;
		public int TcpReadTimeoutMs { get; set; } = ESConsts.ReadRequestTimeout;
	}
}
