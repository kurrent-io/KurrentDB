// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
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
public class when_receiving_a_committed_event_the_projection_with_partitioned_state_should<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private Guid _eventId;

	protected override void Given() {
		_configureBuilderByQuerySource = source => {
			source.FromAll();
			source.AllEvents();
			source.SetByStream();
			source.SetDefinesStateTransform();
			source.SetOutputState();
		};
		TicksAreHandledImmediately();
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override void When() {
		//projection subscribes here
		_eventId = Guid.NewGuid();
		_consumer.HandledMessages.Clear();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", -1, "account-01", -1, false, new TFPos(120, 110), _eventId,
					"handle_this_type", false, "data", "metadata"), _subscriptionId, 0));
	}

	[Test]
	public void request_partition_state_from_the_correct_stream() {
		// 1 - for load state
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>()
				.Count(v => v.EventStreamId == "$projections-projection-account-01-checkpoint"));
	}

	[Test]
	public void update_state_snapshot_is_written_to_the_correct_stream() {
		var writeEvents =
			_writeEventHandler.HandledMessages.Where(v => v.Events.Any(e => e.EventType == "Result")).ToList();
		Assert.AreEqual(1, writeEvents.Count);

		var message = writeEvents[0];
		Assert.AreEqual("$projections-projection-account-01-result", message.EventStreamId);
	}

	[Test]
	public void update_state_snapshot_at_correct_position() {
		var writeEvents =
			_writeEventHandler.HandledMessages.Where(v => v.Events.Any(e => e.EventType == "Result")).ToList();
		Assert.AreEqual(1, writeEvents.Count);

		var metedata = writeEvents[0].Events[0].Metadata
			.ParseCheckpointTagVersionExtraJson(default(ProjectionVersion));

		Assert.AreEqual(120, metedata.Tag.CommitPosition);
		Assert.AreEqual(110, metedata.Tag.PreparePosition);
	}

	[Test]
	public void pass_event_to_state_handler() {
		Assert.AreEqual(1, _stateHandler._eventsProcessed);
		Assert.AreEqual("account-01", _stateHandler._lastProcessedStreamId);
		Assert.AreEqual("handle_this_type", _stateHandler._lastProcessedEventType);
		Assert.AreEqual(_eventId, _stateHandler._lastProcessedEventId);
		//TODO: support sequence numbers here
		Assert.AreEqual("metadata", _stateHandler._lastProcessedMetadata);
		Assert.AreEqual("data", _stateHandler._lastProcessedData);
	}

	[Test]
	public void register_new_partition_state_stream() {
		var writes =
			_writeEventHandler.HandledMessages.Where(v => v.EventStreamId == "$projections-projection-partitions")
				.ToArray();
		Assert.AreEqual(1, writes.Length);
		var write = writes[0];

		Assert.AreEqual(1, write.Events.Length);

		var @event = write.Events[0];

		Assert.AreEqual("account-01", Helper.UTF8NoBom.GetString(@event.Data));
	}
}
