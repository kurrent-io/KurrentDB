// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests.Integration;
using EventStore.Core.Tests.Replication.ReadStream;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.ReadMgr;

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_reading_an_event_committed_on_leader_and_on_followers<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	private CountdownEvent _expectedNumberOfRoleAssignments;

	private string _streamId =
		"when_reading_an_event_committed_on_leader_and_on_followers-" + Guid.NewGuid().ToString();

	private long _commitPosition;
	private long _indexPosition;

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

		var events = new Event[] { new Event(Guid.NewGuid(), "test-type", false, new byte[10], new byte[0]) };
		var writeResult = ReplicationTestHelper.WriteEvent(leader, events, _streamId);
		Assert.AreEqual(OperationResult.Success, writeResult.Result);
		_commitPosition = writeResult.CommitPosition;
		Thread.Sleep(100);
		Assert.IsTrue(_commitPosition <= GetLeader().Db.Config.ReplicationCheckpoint.Read(),
			"Replication checkpoint should be greater than event commit position");
		await base.Given();
		_indexPosition = leader.Db.Config.IndexCheckpoint.Read();
	}

	[Test]
	public void should_be_able_to_read_event_from_all_forward_on_leader() {
		var readResult = ReplicationTestHelper.ReadAllEventsForward(GetLeader(), _commitPosition);
		Assert.AreEqual(1, readResult.Events.Count(x => x.OriginalStreamId == _streamId));
	}

	[Test]
	public void should_be_able_to_read_event_from_all_backward_on_leader() {
		var readResult = ReplicationTestHelper.ReadAllEventsBackward(GetLeader(), _commitPosition);
		Assert.AreEqual(1, readResult.Events.Count(x => x.OriginalStreamId == _streamId));
	}

	[Test]
	public void should_be_able_to_read_event_from_stream_forward_on_leader() {
		var readResult = ReplicationTestHelper.ReadStreamEventsForward(GetLeader(), _streamId);
		Assert.AreEqual(1, readResult.Events.Count());
		Assert.AreEqual(ReadStreamResult.Success, readResult.Result);
	}

	[Test]
	public void should_be_able_to_read_event_from_stream_backward_on_leader() {
		var readResult = ReplicationTestHelper.ReadStreamEventsBackward(GetLeader(), _streamId);
		Assert.AreEqual(ReadStreamResult.Success, readResult.Result);
		Assert.AreEqual(1, readResult.Events.Count());
	}

	[Test]
	public void should_be_able_to_read_event_on_leader() {
		var readResult = ReplicationTestHelper.ReadEvent(GetLeader(), _streamId, 0);
		Assert.AreEqual(ReadEventResult.Success, readResult.Result);
	}

	[Test]
	[Retry(5)]
	public void should_be_able_to_read_event_from_all_forward_on_followers() {
		var followers = GetFollowers();
		var quorum = (followers.Count() + 1) / 2 + 1;
		var successfulReads = 0;
		foreach (var s in followers) {
			AssertEx.IsOrBecomesTrue(()=> s.Db.Config.IndexCheckpoint.Read() >= _indexPosition);
			var readResult = ReplicationTestHelper.ReadAllEventsForward(s, _commitPosition);
			successfulReads += readResult.Events.Count(x => x.OriginalStreamId == _streamId);
		}

		Assert.GreaterOrEqual(successfulReads, quorum - 1);
	}

	[Test]
	[Retry(5)]
	public void should_be_able_to_read_event_from_all_backward_on_followers() {
		var followers = GetFollowers();
		var quorum = (followers.Count() + 1) / 2 + 1;
		var successfulReads = 0;
		foreach (var s in followers) {
			AssertEx.IsOrBecomesTrue(()=> s.Db.Config.IndexCheckpoint.Read() >= _indexPosition);
			var readResult = ReplicationTestHelper.ReadAllEventsBackward(s, _commitPosition);
			successfulReads += readResult.Events.Count(x => x.OriginalStreamId == _streamId);
		}

		Assert.GreaterOrEqual(successfulReads, quorum - 1);
	}

	[Test]
	[Retry(5)]
	public void should_be_able_to_read_event_from_stream_forward_on_followers() {
		var followers = GetFollowers();
		var quorum = (followers.Count() + 1) / 2 + 1;
		var successfulReads = 0;
		foreach (var s in followers) {
			AssertEx.IsOrBecomesTrue(()=> s.Db.Config.IndexCheckpoint.Read() >= _indexPosition);
			var readResult = ReplicationTestHelper.ReadStreamEventsForward(s, _streamId);
			successfulReads += readResult.Events.Count();
			Assert.AreEqual(ReadStreamResult.Success, readResult.Result);
		}

		Assert.GreaterOrEqual(successfulReads, quorum - 1);
	}

	[Test]
	[Retry(5)]
	public void should_be_able_to_read_event_from_stream_backward_on_followers() {
		var followers = GetFollowers();
		var quorum = (followers.Count() + 1) / 2 + 1;
		var successfulReads = 0;
		foreach (var s in followers) {
			AssertEx.IsOrBecomesTrue(()=> s.Db.Config.IndexCheckpoint.Read() >= _indexPosition);
			var readResult = ReplicationTestHelper.ReadStreamEventsBackward(s, _streamId);
			successfulReads += readResult.Events.Count();
		}

		Assert.GreaterOrEqual(successfulReads, quorum - 1);
	}

	[Test]
	[Retry(5)]
	public void should_be_able_to_read_event_on_followers() {
		var followers = GetFollowers();
		var quorum = (followers.Count() + 1) / 2 + 1;
		var successfulReads = 0;
		foreach (var s in followers) {
			AssertEx.IsOrBecomesTrue(()=> s.Db.Config.IndexCheckpoint.Read() >= _indexPosition);
			var readResult = ReplicationTestHelper.ReadEvent(s, _streamId, 0);
			successfulReads += readResult.Result == ReadEventResult.Success ? 1 : 0;
		}

		Assert.GreaterOrEqual(successfulReads, quorum - 1);
	}
}
