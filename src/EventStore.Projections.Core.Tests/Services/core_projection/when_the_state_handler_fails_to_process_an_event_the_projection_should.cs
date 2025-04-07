// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_the_state_handler_fails_to_process_an_event_the_projection_should<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	protected override void Given() {
		ExistingEvent(
			"$projections-projection-result", "Result", @"{""c"": 100, ""p"": 50}", "{}");
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""c"": 100, ""p"": 50}", "{}");
		NoStream("$projections-projection-order");
		AllWritesToSucceed("$projections-projection-order");
	}

	protected override FakeProjectionStateHandler GivenProjectionStateHandler() {
		return new FakeProjectionStateHandler(failOnProcessEvent: true);
	}

	protected override void When() {
		//projection subscribes here
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110),
					Guid.NewGuid(), "handle_this_type", false, "data",
					"metadata"), _subscriptionId, 0));
	}

	[Test]
	public void should_publish_faulted_message() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Faulted>().Count());
	}

	[Test]
	public void not_emit_a_state_updated_event() {
		Assert.AreEqual(0, _writeEventHandler.HandledMessages.OfEventType("Result").Count());
	}
}
