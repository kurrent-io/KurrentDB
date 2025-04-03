// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_an_existing_projection<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private string _testProjectionState = @"{""test"":1}";

	protected override void Given() {
		ExistingEvent(
			"$projections-projection-result", "Result",
			@"{""c"": 100, ""p"": 50}", _testProjectionState);
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""c"": 100, ""p"": 50}", _testProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			@"{""c"": 200, ""p"": 150}", _testProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			@"{""c"": 300, ""p"": 250}", _testProjectionState);
	}

	protected override void When() {
	}


	[Test]
	public void should_subscribe_from_the_last_known_checkpoint_position() {
		Assert.AreEqual(1, _subscribeProjectionHandler.HandledMessages.Count);
		Assert.AreEqual(100, _subscribeProjectionHandler.HandledMessages[0].FromPosition.Position.CommitPosition);
		Assert.AreEqual(50, _subscribeProjectionHandler.HandledMessages[0].FromPosition.Position.PreparePosition);
	}

	[Test]
	public void should_publish_started_message() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Started>().Count());
		var startedMessage = _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Started>().Single();
		Assert.AreEqual(_projectionCorrelationId, startedMessage.ProjectionId);
	}
}
