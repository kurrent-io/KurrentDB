// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_receiving_a_committed_event_the_projection_should<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private Guid _eventId;

	protected override void Given() {
		TicksAreHandledImmediately();
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override void When() {
		//projection subscribes here
		_eventId = Guid.NewGuid();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110), _eventId,
					"handle_this_type", false, "data", "metadata"), _subscriptionId, 0));
	}

	[Test]
	public void update_state_snapshot_at_correct_position() {
		Assert.AreEqual(1, _writeEventHandler.HandledMessages.OfEventType("Result").Count);

		var metedata =
			_writeEventHandler.HandledMessages.OfEventType("Result")[0].Metadata
				.ParseCheckpointTagVersionExtraJson(default(ProjectionVersion));

		Assert.AreEqual(120, metedata.Tag.CommitPosition);
		Assert.AreEqual(110, metedata.Tag.PreparePosition);
	}

	[Test]
	public void pass_event_to_state_handler() {
		Assert.AreEqual(1, _stateHandler._eventsProcessed);
		Assert.AreEqual("/event_category/1", _stateHandler._lastProcessedStreamId);
		Assert.AreEqual("handle_this_type", _stateHandler._lastProcessedEventType);
		Assert.AreEqual(_eventId, _stateHandler._lastProcessedEventId);
		//TODO: support sequence numbers here
		Assert.AreEqual("metadata", _stateHandler._lastProcessedMetadata);
		Assert.AreEqual("data", _stateHandler._lastProcessedData);
	}
}
