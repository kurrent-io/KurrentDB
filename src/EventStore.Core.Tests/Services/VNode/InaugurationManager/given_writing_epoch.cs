// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode.InaugurationManagement;

[TestFixture]
public class given_writing_epoch : InaugurationManagerTests {
	protected override void Given() {
		_sut.Handle(new ElectionMessage.ElectionsDone(123, _epochNumber, _leader));
		_sut.Handle(new SystemMessage.BecomePreLeader(_correlationId1));
		_sut.Handle(new SystemMessage.ChaserCaughtUp(_correlationId1));
		_publisher.Messages.Clear();
	}

	[Test]
	public void when_epoch_written() {
		When(GenEpoch(_epochNumber));
		Assert.AreEqual(2, _publisher.Messages.Count);
		Assert.IsInstanceOf<SystemMessage.EnablePreLeaderReplication>(_publisher.Messages[0]);
		var schedule = AssertEx.IsType<TimerMessage.Schedule>(_publisher.Messages[1]);
		Assert.IsInstanceOf<SystemMessage.CheckInaugurationConditions>(schedule.ReplyMessage);
	}

	[Test]
	public void when_epoch_written_with_unexpected_number() {
		When(GenEpoch(_epochNumber + 1));
		Assert.IsEmpty(_publisher.Messages);
	}

	[Test]
	public void when_epoch_written_and_conditions_instantly_met() {
		CompleteReplication();
		CompleteIndexing();
		When(GenEpoch(_epochNumber));

		Assert.AreEqual(3, _publisher.Messages.Count);
		Assert.IsInstanceOf<SystemMessage.EnablePreLeaderReplication>(_publisher.Messages[0]);
		Assert.IsInstanceOf<SystemMessage.BecomeLeader>(_publisher.Messages[1]);
		Assert.IsInstanceOf<TimerMessage.Schedule>(_publisher.Messages[2]);
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
