// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
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
public class
	when_the_state_handler_does_emit_multiple_subsequent_events_into_the_same_stream_the_projection_should<TLogFormat, TStreamId> :
		TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	protected override void Given() {
		ExistingEvent(
			"$projections-projection-result", "Result", @"{""c"": 100, ""p"": 50}", "{}");
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""c"": 100, ""p"": 50}", "{}");
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override void When() {
		//projection subscribes here
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110),
					Guid.NewGuid(), "emit22_type", false, "data",
					"metadata"), _subscriptionId, 0));
	}


	[Test]
	public void write_events_in_a_single_transaction() {
		Assert.IsTrue(_writeEventHandler.HandledMessages.Any(v => v.Events.Length == 2));
	}

	[Test]
	public void write_all_the_emitted_events() {
		Assert.AreEqual(
			2, _writeEventHandler.HandledMessages.Single(v => v.EventStreamId == "/emit2").Events.Length);
	}

	[Test]
	public void write_events_in_correct_order() {
		Assert.AreEqual(
			FakeProjectionStateHandler._emit1Data,
			Helper.UTF8NoBom.GetString(
				_writeEventHandler.HandledMessages.Single(v => v.EventStreamId == "/emit2").Events[0].Data));
		Assert.AreEqual(
			FakeProjectionStateHandler._emit2Data,
			Helper.UTF8NoBom.GetString(
				_writeEventHandler.HandledMessages.Single(v => v.EventStreamId == "/emit2").Events[1].Data));
	}
}
