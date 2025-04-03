// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Data;

namespace EventStore.Core.Tests.Integration;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_restarting_one_node_at_a_time<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	protected override async Task Given() {
		await base.Given();

		for (int i = 0; i < 9; i++) {
			await Task.Delay(2000); //flaky: temporary fix for getting stable cluster
			
			await _nodes[i % 3].Shutdown(keepDb: true);
			await Task.Delay(2000);
			
			var node = CreateNode(i % 3, _nodeEndpoints[i % 3],
				new[] {_nodeEndpoints[(i+1)%3].HttpEndPoint, _nodeEndpoints[(i+2)%3].HttpEndPoint});
			await node.Start();
			_nodes[i % 3] = node;

			await Task.WhenAll(_nodes.Select(x => x.Started)).WithTimeout(TimeSpan.FromSeconds(30));
		}
	}

	[Test]
	public void cluster_should_stabilize() {
		var leaders = 0;
		var followers = 0;
		var acceptedStates = new[] {VNodeState.Leader, VNodeState.Follower};

		for (int i = 0; i < 3; i++) {
			AssertEx.IsOrBecomesTrue(() => acceptedStates.Contains(_nodes[i].NodeState),
				TimeSpan.FromSeconds(5), $"node {i} failed to become a leader/follower");

			var state = _nodes[i].NodeState;
			if (state == VNodeState.Leader) leaders++;
			else if (state == VNodeState.Follower) followers++;
			else throw new Exception($"node {i} in unexpected state {state}");
		}

		Assert.AreEqual(1, leaders);
		Assert.AreEqual(2, followers);
	}
}
