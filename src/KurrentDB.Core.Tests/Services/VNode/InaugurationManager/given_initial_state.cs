// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messages;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.VNode.InaugurationManager;

[TestFixture]
public class given_initial_state : InaugurationManagerTests {
	protected override void Given() {
		_publisher.Messages.Clear();
	}

	[Test]
	public void when_become_pre_leader() {
		When(new SystemMessage.BecomePreLeader(_correlationId1));
		AssertWaitingForChaser(_correlationId1);
	}

	[Test]
	public void when_become_other_node_state() {
		When(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
		AssertInitial();
	}
}
