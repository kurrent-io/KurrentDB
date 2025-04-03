// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Cluster.Settings;
using EventStore.Core.Data;

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
