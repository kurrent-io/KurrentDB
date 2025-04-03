// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;

namespace EventStore.Core.Messages;

public class ClusterInfoDto {
	public MemberInfoDto[] Members { get; set; }
	public string ServerIp { get; set; }
	public int ServerPort { get; set; }

	public ClusterInfoDto() {
	}

	public ClusterInfoDto(ClusterInfo clusterInfo, EndPoint serverEndPoint) {
		Members = clusterInfo.Members.Select(x => new MemberInfoDto(x)).ToArray();
		ServerIp = serverEndPoint.GetHost();
		ServerPort = serverEndPoint.GetPort();
	}

	public override string ToString() {
		return string.Format("Server: {0}:{1}, Members: [{2}]",
			ServerIp, ServerPort,
			Members != null ? string.Join(",", Members.Select(m => m.ToString())) : "null");
	}
}
