// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using System.Linq;
using EventStore.Core.Tests;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_a_new_projection<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	protected override void Given() {
		NoStream("$projections-projection-result");
		NoStream("$projections-projection-order");
		AllWritesToSucceed("$projections-projection-order");
		NoStream("$projections-projection-checkpoint");
	}

	protected override void When() {
	}

	[Test]
	public void should_subscribe_from_beginning() {
		Assert.AreEqual(1, _subscribeProjectionHandler.HandledMessages.Count);
		Assert.AreEqual(0, _subscribeProjectionHandler.HandledMessages[0].FromPosition.Position.CommitPosition);
		Assert.AreEqual(-1, _subscribeProjectionHandler.HandledMessages[0].FromPosition.Position.PreparePosition);
	}

	[Test]
	public void should_publish_started_message() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Started>().Count());
		var startedMessage = _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Started>().Single();
		Assert.AreEqual(_projectionCorrelationId, startedMessage.ProjectionId);
	}
}
