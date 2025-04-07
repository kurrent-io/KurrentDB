// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_the_projection_with_pending_writes_is_stopped<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	protected override void Given() {
		_checkpointHandledThreshold = 2;
		NoStream("$projections-projection-result");
		NoStream("$projections-projection-order");
		AllWritesToSucceed("$projections-projection-order");
		NoStream("$projections-projection-checkpoint");
		NoStream(FakeProjectionStateHandler._emit1StreamId);
		AllWritesQueueUp();
	}

	protected override void When() {
		//projection subscribes here
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110),
					Guid.NewGuid(), "handle_this_type", false, "data1",
					"metadata"), _subscriptionId, 0));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(140, 130),
					Guid.NewGuid(), "handle_this_type", false, "data2",
					"metadata"), _subscriptionId, 1));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(160, 150),
					Guid.NewGuid(), "handle_this_type", false, "data3",
					"metadata"), _subscriptionId, 2));
		_coreProjection.Stop();
	}

	[Test]
	public void a_projection_checkpoint_event_is_published() {
		AllWriteComplete();
		Assert.AreEqual(
			1,
			_writeEventHandler.HandledMessages.Count(v =>
				v.Events.Any(e => e.EventType == ProjectionEventTypes.ProjectionCheckpoint)));
	}

	[Test]
	public void other_events_are_not_written_after_the_checkpoint_write() {
		AllWriteComplete();
		var index =
			_writeEventHandler.HandledMessages.FindIndex(
				v => v.Events.Any(e => e.EventType == ProjectionEventTypes.ProjectionCheckpoint));
		Assert.AreEqual(index + 1, _writeEventHandler.HandledMessages.Count());
	}
}
