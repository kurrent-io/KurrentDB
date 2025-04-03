// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using NUnit.Framework;
using EventStore.Core.Tests.Integration;
using EventStore.Core.Messages;
using EventStore.Core.Data;

namespace EventStore.Core.Tests.Replication.ReadStream;

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_subscribed_to_stream_on_leader_and_event_is_replicated_to_followers<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	private const string _streamId = "test-stream";
	private CountdownEvent _expectedNumberOfRoleAssignments;
	private CountdownEvent _subscriptionsConfirmed;
	private TestSubscription<TLogFormat, TStreamId> _leaderSubscription;
	private List<TestSubscription<TLogFormat, TStreamId>> _followerSubscriptions;

	private TimeSpan _timeout = TimeSpan.FromSeconds(5);

	protected override void BeforeNodesStart() {
		_nodes.ToList().ForEach(x =>
			x.Node.MainBus.Subscribe(new AdHocHandler<SystemMessage.StateChangeMessage>(Handle)));
		_expectedNumberOfRoleAssignments = new CountdownEvent(3);
		base.BeforeNodesStart();
	}

	private void Handle(SystemMessage.StateChangeMessage msg) {
		switch (msg.State) {
			case Data.VNodeState.Leader:
				_expectedNumberOfRoleAssignments.Signal();
				break;
			case Data.VNodeState.Follower:
				_expectedNumberOfRoleAssignments.Signal();
				break;
		}
	}

	protected override async Task Given() {
		_expectedNumberOfRoleAssignments.Wait(5000);

		var leader = GetLeader();
		Assert.IsNotNull(leader, "Could not get leader node");

		// Set the checkpoint so the check is not skipped
		leader.Db.Config.ReplicationCheckpoint.Write(0);

		_subscriptionsConfirmed = new CountdownEvent(3);
		_leaderSubscription = new TestSubscription<TLogFormat, TStreamId>(leader, 1, _streamId, _subscriptionsConfirmed);
		_leaderSubscription.CreateSubscription();

		_followerSubscriptions = new List<TestSubscription<TLogFormat, TStreamId>>();
		var followers = GetFollowers();
		foreach (var s in followers) {
			var followerSubscription = new TestSubscription<TLogFormat, TStreamId>(s, 1, _streamId, _subscriptionsConfirmed);
			_followerSubscriptions.Add(followerSubscription);
			followerSubscription.CreateSubscription();
		}

		if (!_subscriptionsConfirmed.Wait(_timeout)) {
			Assert.Fail($"Timed out waiting for subscriptions to confirm, confirmed {_subscriptionsConfirmed.CurrentCount} need {_subscriptionsConfirmed.InitialCount}.");
		}

		var events = new Event[] { new Event(Guid.NewGuid(), "test-type", false, new byte[10], new byte[0]) };
		var writeResult = ReplicationTestHelper.WriteEvent(leader, events, _streamId);
		Assert.AreEqual(OperationResult.Success, writeResult.Result);

		await base.Given();
		var replicas = GetFollowers();
		AssertEx.IsOrBecomesTrue(
			() => {
				var leaderIndex = leader.Db.Config.IndexCheckpoint.Read();
				return replicas[0].Db.Config.IndexCheckpoint.Read() == leaderIndex &&
				       replicas[1].Db.Config.IndexCheckpoint.Read() == leaderIndex;

			},
			timeout:TimeSpan.FromSeconds(2));
	}

	[Test]
	public void should_receive_event_on_leader() {
		Assert.IsTrue(_leaderSubscription.EventAppeared.Wait(2000));
	}

	[Test]
	public void should_receive_event_on_followers() {
		if (!(_followerSubscriptions[0].EventAppeared.Wait(2000) && _followerSubscriptions[1].EventAppeared.Wait(2000))) {
			Assert.Fail("Timed out waiting for follower subscriptions to get events");
		}
	}
}
