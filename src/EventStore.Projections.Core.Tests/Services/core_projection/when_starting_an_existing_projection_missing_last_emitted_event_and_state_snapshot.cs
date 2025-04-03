// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
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
public class when_starting_an_existing_projection_missing_last_emitted_event_and_state_snapshot<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private readonly Guid _causedByEventId = Guid.NewGuid();

	protected override void Given() {
		ExistingEvent(
			"$projections-projection-result", "Result", @"{""c"": 100, ""p"": 50}", "{}");
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""c"": 100, ""p"": 50}", "{}");

		ExistingEvent(
			FakeProjectionStateHandler._emit1StreamId, FakeProjectionStateHandler._emit1EventType,
			@"{""c"": 120, ""p"": 110}", FakeProjectionStateHandler._emit1Data);
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override void When() {
		//projection subscribes here
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110),
					_causedByEventId, "emit12_type", false, "data",
					"metadata"), _subscriptionId, 0));
	}

	[Test]
	public void should_write_second_emitted_event_and_state_snapshot() {
		Assert.AreEqual(1, _writeEventHandler.HandledMessages.OfEventType("Result").Count);
		Assert.AreEqual(
			1, _writeEventHandler.HandledMessages.OfEventType(FakeProjectionStateHandler._emit2EventType).Count);

		Assert.IsTrue(
			_writeEventHandler.HandledMessages.Any(
				v => Helper.UTF8NoBom.GetString(v.Events[0].Data) == FakeProjectionStateHandler._emit2Data));
		Assert.IsTrue(_writeEventHandler.HandledMessages.Any(v => v.Events[0].EventType == "Result"));
	}
}
