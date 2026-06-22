// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Integration;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_running_cluster_with_disable_tls<TLogFormat, TStreamId>
	: SpecificationWithDirectoryPerTestFixture {

	private readonly MiniClusterNode<TLogFormat, TStreamId>[] _nodes =
		new MiniClusterNode<TLogFormat, TStreamId>[3];

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		MiniNodeLogging.Setup();

		var sockets = new List<Socket>();
		var endpoints = new (IPEndPoint intTcp, IPEndPoint extTcp, IPEndPoint http)[3];
		try {
			for (int i = 0; i < 3; i++)
				endpoints[i] = AllocateEndpoints(sockets);
		} finally {
			foreach (var s in sockets) s.Dispose();
		}

		var allPorts = endpoints.SelectMany(e => new[] { e.intTcp.Port, e.extTcp.Port, e.http.Port }).ToList();
		Assert.AreEqual(allPorts.Count, allPorts.Distinct().Count(), "Allocated duplicate ports");

		var clusterSecret = Guid.NewGuid().ToString("N");

		for (int i = 0; i < 3; i++) {
			var peers = new EndPoint[] {
				endpoints[(i + 1) % 3].http,
				endpoints[(i + 2) % 3].http,
			};
			_nodes[i] = new MiniClusterNode<TLogFormat, TStreamId>(
				PathName, i,
				endpoints[i].intTcp, endpoints[i].extTcp, endpoints[i].http,
				gossipSeeds: peers,
				inMemDb: false,
				disableTls: true,
				clusterSecret: clusterSecret);
		}

		await Task.WhenAll(_nodes.Select(n => n.Start()));
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await Task.WhenAll(_nodes.Select(n => n.Shutdown()));
		MiniNodeLogging.Clear();
		await base.TestFixtureTearDown();
	}

	[Test]
	public void cluster_elects_a_leader_and_followers() {
		// With --disable-tls, nodes must still be able to authenticate to each
		// other for gossip and elections — otherwise inter-node calls are
		// rejected as unauthenticated and the cluster never converges.
		AssertEx.IsOrBecomesTrue(
			() => _nodes.Any(n => n.NodeState == VNodeState.Leader),
			timeout: TimeSpan.FromSeconds(30),
			msg: "No leader elected. Node states: " +
				string.Join(", ", _nodes.Select(n => n.NodeState)));

		AssertEx.IsOrBecomesTrue(
			() => _nodes.Any(n => n.NodeState == VNodeState.Follower),
			timeout: TimeSpan.FromSeconds(30),
			msg: "No follower joined. Node states: " +
				string.Join(", ", _nodes.Select(n => n.NodeState)));
	}

	private static (IPEndPoint intTcp, IPEndPoint extTcp, IPEndPoint http) AllocateEndpoints(List<Socket> sockets) {
		var loopback = new IPEndPoint(IPAddress.Loopback, 0);
		var intTcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		var extTcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		var http = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		intTcp.Bind(loopback); sockets.Add(intTcp);
		extTcp.Bind(loopback); sockets.Add(extTcp);
		http.Bind(loopback); sockets.Add(http);
		return (
			new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)intTcp.LocalEndPoint!).Port),
			new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)extTcp.LocalEndPoint!).Port),
			new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)http.LocalEndPoint!).Port));
	}
}
