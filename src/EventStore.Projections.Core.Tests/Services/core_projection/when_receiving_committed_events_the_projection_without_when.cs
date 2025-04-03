// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public abstract class specification_with_query_without_when<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	protected Guid _eventId;

	protected override bool GivenCheckpointsEnabled() {
		return false;
	}

	protected override bool GivenEmitEventEnabled() {
		return false;
	}

	protected override bool GivenStopOnEof() {
		return true;
	}

	protected override int GivenPendingEventsThreshold() {
		return 0;
	}

	protected override ProjectionProcessingStrategy GivenProjectionProcessingStrategy() {
		return CreateQueryProcessingStrategy();
	}

	protected override void Given() {
		_checkpointHandledThreshold = 0;
		_checkpointUnhandledBytesThreshold = 0;
		_configureBuilderByQuerySource = source => {
			source.FromAll();
			source.AllEvents();
			source.NoWhen();
		};
		TicksAreHandledImmediately();
		NoOtherStreams();
		AllWritesSucceed();
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_receiving_committed_events_the_projection_without_when<TLogFormat, TStreamId> : specification_with_query_without_when<TLogFormat, TStreamId> {
	protected override void When() {
		//projection subscribes here
		_eventId = Guid.NewGuid();
		_consumer.HandledMessages.Clear();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", 1, "account-01", 1, false, new TFPos(120, 110), _eventId, "handle_this_type",
					false, "data1", "metadata"), _subscriptionId, 0));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-02", 2, "account-02", 2, false, new TFPos(140, 130), _eventId, "handle_this_type",
					false, "data2", "metadata"), _subscriptionId, 1));
	}

	[Test]
	public void update_state_snapshots_are_written_to_the_correct_stream() {
		var writeEvents =
			_writeEventHandler.HandledMessages.Where(v => v.Events.Any(e => e.EventType == "Result")).ToList();
		Assert.AreEqual(2, writeEvents.Count);
		Assert.AreEqual("$projections-projection-result", writeEvents[0].EventStreamId);
		Assert.AreEqual("$projections-projection-result", writeEvents[1].EventStreamId);
		Assert.AreEqual("data1", Encoding.UTF8.GetString(writeEvents[0].Events[0].Data));
		Assert.AreEqual("data2", Encoding.UTF8.GetString(writeEvents[1].Events[0].Data));
	}
}


[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_handling_event_does_not_change_state_the_projection_without_when<TLogFormat, TStreamId> : specification_with_query_without_when<TLogFormat, TStreamId> {
	protected override void When() {
		//projection subscribes here
		_eventId = Guid.NewGuid();
		_consumer.HandledMessages.Clear();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", 1, "account-01", 1, false, new TFPos(120, 110), _eventId, "handle_this_type",
					false, "data1", "metadata"), _subscriptionId, 0));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", 2, "account-01", 2, false, new TFPos(140, 130), _eventId, "handle_this_type",
					false, "data1", "metadata"), _subscriptionId, 1));
	}

	[Test, Ignore("To be fixed")]
	public void result_events_are_produced_for_each_received_event() {
		var writeEvents =
			_writeEventHandler.HandledMessages.Where(v => v.Events.Any(e => e.EventType == "Result")).ToList();
		Assert.AreEqual(2, writeEvents.Count);
		Assert.AreEqual("$projections-projection-result", writeEvents[0].EventStreamId);
		Assert.AreEqual("$projections-projection-result", writeEvents[1].EventStreamId);
		Assert.AreEqual("data1", Encoding.UTF8.GetString(writeEvents[0].Events[0].Data));
		Assert.AreEqual("data1", Encoding.UTF8.GetString(writeEvents[1].Events[0].Data));
	}

	[Test]
	public void no_result_removed_events_are_produced() {
		var writeEvents =
			_writeEventHandler.HandledMessages.Where(v => v.Events.Any(e => e.EventType == "ResultRemoved"))
				.ToList();
		Assert.AreEqual(0, writeEvents.Count);
	}
}
