// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_the_state_handler_does_process_an_event_the_projection_should<TLogFormat, TStreamId> :
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
	public void write_the_new_state_snapshot() {
		Assert.AreEqual(1, _writeEventHandler.HandledMessages.OfEventType("Result").Count);

		var data = Helper.UTF8NoBom.GetString(_writeEventHandler.HandledMessages.OfEventType("Result")[0].Data);
		Assert.AreEqual("data", data);
	}

	[Test]
	public void emit_a_state_updated_event() {
		Assert.AreEqual(1, _writeEventHandler.HandledMessages.OfEventType("Result").Count);

		var @event = _writeEventHandler.HandledMessages.OfEventType("Result")[0];
		Assert.AreEqual("Result", @event.EventType);
	}
}
