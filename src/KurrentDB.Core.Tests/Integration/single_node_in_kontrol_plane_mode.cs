// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace KurrentDB.Core.Tests.Integration;

// MiniClusterNode rather than MiniNode: MiniNode serves HTTP through an in-memory TestServer, and the
// Kontrol Plane dials its own gRPC API over a real socket, so there would be nothing listening.
[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class single_node_in_kontrol_plane_mode<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private MiniClusterNode<TLogFormat, TStreamId> _node;
	private bool _electionsRan;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		MiniNodeLogging.Setup();

		try {
			var ip = IPAddress.Loopback;
			_node = new MiniClusterNode<TLogFormat, TStreamId>(
				PathName,
				debugIndex: 0,
				internalTcp: new(ip, PortsHelper.GetAvailablePort(ip)),
				externalTcp: new(ip, PortsHelper.GetAvailablePort(ip)),
				httpEndPoint: new(ip, PortsHelper.GetAvailablePort(ip)),
				gossipSeeds: [],
				// Validation rejects an in-memory database for a node taking part in either plane
				inMemDb: false,
				kontrolPlaneMode: true,
				kontrollerPort: PortsHelper.GetAvailablePort(ip),
				// A cluster of one is its own Kontrol Plane, so it cold starts and needs no seed
				clusterSize: 1);

			_node.Node.MainBus.Subscribe(new AdHocHandler<ElectionMessage.ElectionsDone>(_ =>
				_electionsRan = true));

			await _node.Start();

			// Started completes when a node becomes leader/follower/ror
			await _node.Started.WithTimeout(TimeSpan.FromMinutes(1), onFail: MiniNodeLogging.WriteLogs);
		} catch {
			MiniNodeLogging.WriteLogs();
			throw;
		}
	}

	[Test]
	public void the_node_is_appointed_leader() =>
		Assert.AreEqual(VNodeState.Leader, _node.NodeState);

	// The observable difference from legacy mode: a single node on elections still holds one and elects
	// itself, whereas here the node's own Kontrol Plane appoints it.
	[Test]
	public void no_election_is_held() =>
		Assert.IsFalse(_electionsRan, "An election was held, so the node is not using the Kontrol Plane");

	// Proves the appointed leader can actually write, rather than only reporting that it leads
	[Test]
	public async Task the_admin_user_is_created() =>
		await _node.AdminUserCreated.WithTimeout(TimeSpan.FromMinutes(1), onFail: MiniNodeLogging.WriteLogs);

	[TearDown]
	public void AfterEachTest() {
		if (TestContext.CurrentContext.Result.Outcome.Status is TestStatus.Failed)
			MiniNodeLogging.WriteLogs();
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		if (_node is not null)
			await _node.Shutdown();

		MiniNodeLogging.Clear();
		await base.TestFixtureTearDown();
	}
}
