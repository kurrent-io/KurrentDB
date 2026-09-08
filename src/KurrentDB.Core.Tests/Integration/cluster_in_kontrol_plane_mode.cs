// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Integration;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class cluster_in_kontrol_plane_mode<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	// In Kontrol Plane mode the elections service is not constructed at all, so a cluster that reaches
	// Leader and Follower can only have got there by being appointed.
	[Test]
	public void the_kontrol_plane_appoints_one_leader_and_the_rest_follow() {
		AssertEx.IsOrBecomesTrue(
			() => _nodes.Count(x => x.NodeState is VNodeState.Leader) == 1
			   && _nodes.Count(x => x.NodeState is VNodeState.Follower) == 2,
			timeout: TimeSpan.FromSeconds(60),
			onFail: () => {
				TestContext.Out.WriteLine($"Node states: {States()}");
				MiniNodeLogging.WriteLogs();
			},
			msg: "Expected one leader and two followers");
	}

	protected override MiniClusterNode<TLogFormat, TStreamId> CreateNode(
		int index, Endpoints endpoints, EndPoint[] gossipSeeds, bool wait = true) => new(
			pathname: PathName,
			debugIndex: index,
			internalTcp: endpoints.InternalTcp,
			externalTcp: endpoints.ExternalTcp,
			httpEndPoint: endpoints.HttpEndPoint,
			subsystems: [],
			gossipSeeds: gossipSeeds,
			inMemDb: false,
			kontrolPlaneMode: true,
			kontrollerPort: endpoints.Kontroller.Port,
			kontrolPlaneBootstrapSeed: _nodeEndpoints
				.Where((_, i) => i != index)
				.Select(x => (EndPoint)x.Kontroller)
				.ToArray());

	private string States() => string.Join(", ", _nodes.Select(x => x.NodeState));
}
