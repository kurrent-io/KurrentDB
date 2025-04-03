// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode.InaugurationManagement;


[TestFixture]
public class given_waiting_for_chaser : InaugurationManagerTests {
	protected override void Given() {
		_sut.Handle(new ElectionMessage.ElectionsDone(123, _epochNumber, _leader));
		_sut.Handle(new SystemMessage.BecomePreLeader(_correlationId1));
		_publisher.Messages.Clear();
	}

	[Test]
	public void when_chaser_caught_up() {
		When(new SystemMessage.ChaserCaughtUp(_correlationId1));
		Assert.AreEqual(1, _publisher.Messages.Count);
		var writeEpoch = AssertEx.IsType<SystemMessage.WriteEpoch>(_publisher.Messages[0]);
		Assert.AreEqual(_epochNumber, writeEpoch.EpochNumber);
	}

	[Test]
	public void when_chaser_caught_up_with_unknown_correlation_id() {
		When(new SystemMessage.ChaserCaughtUp(_correlationId2));
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
