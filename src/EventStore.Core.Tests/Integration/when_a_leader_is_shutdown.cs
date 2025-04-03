// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Tests.Integration;

[Category("LongRunning"), Ignore("Flaky test - e.g. if multiple elections take place")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_leader_is_shutdown<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	private List<Guid> _epochIds = new List<Guid>();
	private List<string> _roleAssignments = new List<string>();
	private CountdownEvent _expectedNumberOfEvents;
	private object _lock = new object();

	protected override void BeforeNodesStart() {
		_nodes.ToList().ForEach(x => {
			x.Node.MainBus.Subscribe(new AdHocHandler<SystemMessage.BecomeLeader>(Handle));
			x.Node.MainBus.Subscribe(new AdHocHandler<SystemMessage.BecomeFollower>(Handle));
			x.Node.MainBus.Subscribe(new AdHocHandler<SystemMessage.EpochWritten>(Handle));
		});

		_expectedNumberOfEvents = new CountdownEvent(3 /*role assignments*/ + 1 /*epoch write*/);
		base.BeforeNodesStart();
	}

	protected override async Task Given() {
		_expectedNumberOfEvents.Wait(5000);
		var leader = _nodes.First(x => x.NodeState == Data.VNodeState.Leader);
		await ShutdownNode(leader.DebugIndex);
		_expectedNumberOfEvents = new CountdownEvent(2 /*role assignments*/ + 1 /*epoch write*/);
		_expectedNumberOfEvents.Wait(5000);
		await base.Given();
	}

	private void Handle(SystemMessage.BecomeLeader msg) {
		lock (_lock) {
			_roleAssignments.Add("leader");
		}

		_expectedNumberOfEvents?.Signal();
	}

	private void Handle(SystemMessage.BecomeFollower msg) {
		lock (_lock) {
			_roleAssignments.Add("follower");
		}

		_expectedNumberOfEvents?.Signal();
	}

	private void Handle(SystemMessage.EpochWritten msg) {
		lock (_lock) {
			_epochIds.Add(msg.Epoch.EpochId);
		}

		_expectedNumberOfEvents?.Signal();
	}

	[Test]
	public void should_assign_leader_and_follower_roles_correctly() {
		Assert.AreEqual(5, _roleAssignments.Count());

		Assert.AreEqual(1, _roleAssignments.Take(3).Where(x => x.Equals("leader")).Count());
		Assert.AreEqual(2, _roleAssignments.Take(3).Where(x => x.Equals("follower")).Count());

		//after shutting down
		Assert.AreEqual(1, _roleAssignments.Skip(3).Take(2).Where(x => x.Equals("leader")).Count());
		Assert.AreEqual(1, _roleAssignments.Skip(3).Take(2).Where(x => x.Equals("follower")).Count());
	}

	[Test]
	public void should_have_two_unique_epoch_writes() {
		Assert.AreEqual(2, _epochIds.Distinct().Count());
	}
}
