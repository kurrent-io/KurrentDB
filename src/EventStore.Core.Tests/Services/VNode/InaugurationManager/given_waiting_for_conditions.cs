// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode.InaugurationManagement;

[TestFixture]
public class given_waiting_for_conditions : InaugurationManagerTests {
	protected override void Given() {
		_sut.Handle(new ElectionMessage.ElectionsDone(123, _epochNumber, _leader));
		_sut.Handle(new SystemMessage.BecomePreLeader(_correlationId1));
		_sut.Handle(new SystemMessage.ChaserCaughtUp(_correlationId1));
		_sut.Handle(GenEpoch(_epochNumber));
		_publisher.Messages.Clear();
	}

	[Test]
	public void when_transition_triggered_by_indexedto() {
		ProgressReplication();
		ProgressIndexing();
		CompleteReplication();
		CompleteIndexing();
		Assert.IsEmpty(_publisher.Messages);

		When(new ReplicationTrackingMessage.IndexedTo(_indexCheckpoint.Read()));

		AssertSentBecomeLeader();
	}

	[Test]
	public void when_transition_triggered_by_replicatedto() {
		ProgressReplication();
		ProgressIndexing();
		CompleteReplication();
		CompleteIndexing();
		Assert.IsEmpty(_publisher.Messages);

		When(new ReplicationTrackingMessage.ReplicatedTo(_replicationCheckpoint.Read()));

		AssertSentBecomeLeader();
	}

	[Test]
	public void when_transition_triggered_by_checkconditions() {
		CompleteReplication();
		CompleteIndexing();
		Assert.IsEmpty(_publisher.Messages);

		When(new SystemMessage.CheckInaugurationConditions());

		Assert.AreEqual(2, _publisher.Messages.Count);
		Assert.IsInstanceOf<TimerMessage.Schedule>(_publisher.Messages[0]);
		_publisher.Messages.RemoveAt(0);

		AssertSentBecomeLeader();
	}

	[Test]
	public void cant_become_leader_twice() {
		CompleteReplication();
		CompleteIndexing();
		Assert.IsEmpty(_publisher.Messages);

		When(new ReplicationTrackingMessage.ReplicatedTo(_replicationCheckpoint.Read()));
		When(new SystemMessage.CheckInaugurationConditions());

		AssertSentBecomeLeader();
	}

	[Test]
	public void when_epoch_written() {
		When(GenEpoch(_epochNumber));
		Assert.IsEmpty(_publisher.Messages);
	}

	[Test]
	public void when_chaser_caught_up() {
		When(new SystemMessage.ChaserCaughtUp(_correlationId1));
		Assert.IsEmpty(_publisher.Messages);
	}

	[Test]
	public void when_become_pre_leader() {
		When(new SystemMessage.BecomePreLeader(_correlationId2));
		AssertWaitingForChaser(_correlationId2);
	}

	[Test]
	public void when_become_other_node_state() {
		When(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
		AssertInitial();
	}
}
