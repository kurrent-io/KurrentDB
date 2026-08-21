// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using FluentAssertions;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Gossip;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.GossipService;

[TestFixture]
public class ClusterMixedInternalTlsLoggerTests {
	private static readonly DateTime Now = DateTime.UtcNow;

	// A node configures exactly one of its internal endpoints, so which one is set is its TLS setting.
	private static MemberInfo Node(int index, bool usesTls, bool isAlive = true,
		string esVersion = VersionInfo.DefaultVersion) {
		var endPoint = new IPEndPoint(IPAddress.Loopback, 1110 + index);
		return MemberInfo.ForVNode(
			instanceId: Guid.NewGuid(),
			timeStamp: Now,
			state: VNodeState.Follower,
			isAlive: isAlive,
			internalTcpEndPoint: usesTls ? null : endPoint,
			internalSecureTcpEndPoint: usesTls ? endPoint : null,
			externalTcpEndPoint: null,
			externalSecureTcpEndPoint: null,
			httpEndPoint: new IPEndPoint(IPAddress.Loopback, 2110 + index),
			advertiseHostToClientAs: null,
			advertiseHttpPortToClientAs: 0,
			advertiseTcpPortToClientAs: 0,
			lastCommitPosition: -1,
			writerCheckpoint: -1,
			chaserCheckpoint: -1,
			epochPosition: -1,
			epochNumber: -1,
			epochId: Guid.Empty,
			nodePriority: 0,
			isReadOnlyReplica: false,
			esVersion: esVersion);
	}

	// A gossip seed before it has been replaced by real gossip about that node.
	private static MemberInfo Seed(int index) => MemberInfo.ForManager(
		Guid.NewGuid(), Now, isAlive: true, new IPEndPoint(IPAddress.Loopback, 2110 + index));

	private static int DistinctSettings(params MemberInfo[] members) =>
		ClusterMixedInternalTlsLogger.GetReplicationTls(new ClusterInfo(members))
			.Select(static x => x.UsesTls)
			.Distinct()
			.Count();

	[Test]
	public void agrees_when_every_node_uses_tls() {
		DistinctSettings(Node(1, usesTls: true), Node(2, usesTls: true), Node(3, usesTls: true))
			.Should().Be(1);
	}

	[Test]
	public void agrees_when_no_node_uses_tls() {
		DistinctSettings(Node(1, usesTls: false), Node(2, usesTls: false), Node(3, usesTls: false))
			.Should().Be(1);
	}

	[Test]
	public void disagrees_when_one_node_differs() {
		DistinctSettings(Node(1, usesTls: true), Node(2, usesTls: true), Node(3, usesTls: false))
			.Should().Be(2);
	}

	[Test]
	public void ignores_dead_nodes() {
		// A node that is gone tells us nothing about the cluster's current configuration.
		DistinctSettings(Node(1, usesTls: true), Node(2, usesTls: false, isAlive: false))
			.Should().Be(1);
	}

	[Test]
	public void ignores_gossip_seeds() {
		// ForManager fabricates a plain internal endpoint, which would read as "no TLS" and warn
		// spuriously until real gossip about that node arrives.
		DistinctSettings(Node(1, usesTls: true), Seed(2))
			.Should().Be(1);
	}

	[Test]
	public void ignores_nodes_of_unknown_version() {
		// We have no gossip from the node itself, so its endpoints are not its own report.
		DistinctSettings(Node(1, usesTls: true), Node(2, usesTls: false, esVersion: VersionInfo.UnknownVersion))
			.Should().Be(1);
	}

	[Test]
	public void reports_each_node_it_considers() {
		var nodeOne = Node(1, usesTls: true);
		var nodeTwo = Node(2, usesTls: false);

		ClusterMixedInternalTlsLogger.GetReplicationTls(new ClusterInfo(nodeOne, nodeTwo, Seed(3)))
			.Should().BeEquivalentTo(new[] {
				(HttpEndPoint: nodeOne.HttpEndPoint, UsesTls: true),
				(HttpEndPoint: nodeTwo.HttpEndPoint, UsesTls: false),
			});
	}

	[Test]
	public void tolerates_gossip_without_cluster_info() {
		var sut = new ClusterMixedInternalTlsLogger();
		Assert.DoesNotThrow(() => sut.Handle(new GossipMessage.GossipUpdated(null)));
	}
}
