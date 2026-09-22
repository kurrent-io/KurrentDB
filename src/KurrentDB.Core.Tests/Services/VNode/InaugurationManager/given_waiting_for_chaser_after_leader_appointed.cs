// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messages;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.VNode.InaugurationManager;

// As given_waiting_for_chaser, but the leader was appointed by the Kontrol Plane rather than
// elected. The epoch to write comes from a different field on a different message, so it is worth
// pinning separately.
[TestFixture]
public class given_waiting_for_chaser_after_leader_appointed : InaugurationManagerTests {
	protected override void Given() {
		_sut.Handle(new ElectionMessage.LeaderAppointed(_epochNumber, _leader.ToLite()));
		_sut.Handle(new SystemMessage.BecomePreLeader(_correlationId1));
		_publisher.Messages.Clear();
	}

	[Test]
	public void when_chaser_caught_up_should_write_the_appointed_epoch() {
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
	public void when_become_other_node_state() {
		When(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
		AssertInitial();
	}
}
