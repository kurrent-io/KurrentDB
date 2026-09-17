// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using KurrentDB.Common.Log;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.Gossip;

/// <summary>
/// Warns when the cluster's nodes do not agree on whether replication uses TLS.
/// </summary>
public class ClusterMixedInternalTlsLogger : IHandle<GossipMessage.GossipUpdated> {
	private static readonly ThrottledLog<ClusterMixedInternalTlsLogger> Log = new(TimeSpan.FromMinutes(1));

	public void Handle(GossipMessage.GossipUpdated message) {
		if (message.ClusterInfo is not { } cluster)
			return;

		var nodes = GetReplicationTls(cluster);
		if (nodes.Select(static x => x.UsesTls).Distinct().Count() <= 1)
			return;

		var described = nodes.Select(static x => $"({x.HttpEndPoint},{(x.UsesTls ? "TLS" : "no TLS")})");
		Log.Warning($"CLUSTER NODES DISAGREE ON REPLICATION TLS [ {string.Join(", ", described)} ]. " +
					"Replication cannot be established between nodes configured differently.");
	}

	// Whether each node replicates over TLS. A node sets exactly one of its internal endpoints
	// (VNodeInfo enforces it), so which one tells us its setting.
	//
	// Gossip seeds are skipped: MemberInfo.ForManager stands them up as alive with only a plain
	// internal endpoint, which would read as "no TLS" until real gossip replaces them.
	public static IReadOnlyList<(EndPoint HttpEndPoint, bool UsesTls)> GetReplicationTls(ClusterInfo cluster) => cluster
		.Members
		.Where(static member => member.IsAlive
							 && member.State is not VNodeState.Manager
							 && !VersionInfo.UnknownVersion.Equals(member.ESVersion))
		.Select(static member => (member.HttpEndPoint, member.InternalSecureTcpEndPoint is not null))
		.ToList();
}
