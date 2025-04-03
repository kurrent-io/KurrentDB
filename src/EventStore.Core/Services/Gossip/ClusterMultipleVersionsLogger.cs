// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Common.Log;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;

namespace EventStore.Core.Services.Gossip;

public class ClusterMultipleVersionsLogger : IHandle<GossipMessage.GossipUpdated> {
	private static readonly ThrottledLog<ClusterMultipleVersionsLogger> Log = new(TimeSpan.FromMinutes(1));

	public void Handle(GossipMessage.GossipUpdated message) {
		ClusterInfo updatedCluster = message.ClusterInfo;
		if (updatedCluster == null) {
			return;
		}
		Dictionary<EndPoint, string> ipAddressVsVersion = GetIPAddressVsVersion(updatedCluster, out int numDistinctKnownVersions);
		if (numDistinctKnownVersions > 1) {
			IEnumerable<string> ipAndVersion = ipAddressVsVersion.Select(keyvalue => $"({keyvalue.Key},{keyvalue.Value})");
			Log.Warning($"MULTIPLE DB VERSIONS ON CLUSTER NODES FOUND [ {string.Join(", ", ipAndVersion)} ]");
		}
	}

	public static Dictionary<EndPoint, string> GetIPAddressVsVersion(ClusterInfo cluster, out int numDistinctKnownVersions) {
		var aliveMembers = cluster.Members.Where(memberInfo => memberInfo.IsAlive).ToList();
		numDistinctKnownVersions = aliveMembers.Select(memberInfo => memberInfo.ESVersion)
			.Where(esVersion => !VersionInfo.UnknownVersion.Equals(esVersion)).Distinct().Count();

		return aliveMembers.ToDictionary(memberInfo => memberInfo.HttpEndPoint, memberInfo => memberInfo.ESVersion);
	}
}
