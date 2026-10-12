// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Configuration;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Core;

// Still needed for gossip and stats.
public class NodeTcpOptions : IConfigurationBinder<NodeTcpOptions> {
	public int NodeTcpPort { get; set; } = 1113;
	public bool EnableExternalTcp { get; set; }
	public int? NodeTcpPortAdvertiseAs { get; set; }

	static NodeTcpOptions IConfigurationBinder<NodeTcpOptions>.Bind(IConfiguration configuration)
		=> configuration.Get<NodeTcpOptions>()!;
}
