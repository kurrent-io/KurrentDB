// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal class NodeConnectionInfo {
	public IPAddress IpAddress { get; private set; }
	public int TcpPort { get; private set; }
	public int HttpPort { get; private set; }

	public NodeConnectionInfo(IPAddress ipAddress, int tcpPort, int httPort) {
		IpAddress = ipAddress;
		TcpPort = tcpPort;
		HttpPort = httPort;
	}

	public override string ToString() {
		return string.Format("[{0}:{1}:{2}]", IpAddress, TcpPort, HttpPort);
	}
}
