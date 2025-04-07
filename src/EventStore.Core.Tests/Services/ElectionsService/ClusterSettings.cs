// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Net;

namespace EventStore.Core.Tests.Services.ElectionsService;

public sealed class ClusterSettings {
	public string ClusterDns { get; }

	public ClusterVNodeSettings Self { get; }
	public ClusterVNodeSettings[] GroupMembers { get; }

	public IPEndPoint ClusterManager { get; }

	public int ClusterNodesCount { get; }

	public ClusterSettings(string clusterDns,
		IPEndPoint clusterManager,
		ClusterVNodeSettings self,
		ClusterVNodeSettings[] groupMembers,
		int expectedNodesCount) {
		if (string.IsNullOrWhiteSpace(clusterDns))
			throw new ArgumentException($"Wrong cluster DNS name: {clusterDns}", clusterDns);
		if (self == null)
			throw new ArgumentNullException(nameof(self));
		if (groupMembers == null)
			throw new ArgumentNullException(nameof(groupMembers));
		if (clusterManager == null)
			throw new ArgumentNullException(nameof(clusterManager));

		ClusterDns = clusterDns;
		Self = self;
		GroupMembers = groupMembers;
		ClusterManager = clusterManager;
		ClusterNodesCount = expectedNodesCount;
	}
}
