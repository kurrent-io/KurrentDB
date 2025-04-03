// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_receiving_committed_events_the_projection_with_partitioned_state_should<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private Guid _eventId;

	protected override void Given() {
		_configureBuilderByQuerySource = source => {
			source.FromAll();
			source.AllEvents();
			source.SetByStream();
			source.SetDefinesFold();
			source.SetOutputState();
		};
		TicksAreHandledImmediately();
		NoOtherStreams();
		AllWritesSucceed();
	}

	protected override void When() {
		//projection subscribes here
		_eventId = Guid.NewGuid();
		_consumer.HandledMessages.Clear();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", 1, "account-01", 1, false, new TFPos(120, 110), _eventId,
					"handle_this_type", false, "data1", "metadata"), _subscriptionId, 0));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-02", 2, "account-02", 2, false, new TFPos(140, 130), _eventId,
					"handle_this_type", false, "data2", "metadata"), _subscriptionId, 1));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", 2, "account-01", 2, false, new TFPos(160, 150), _eventId, "append", false,
					"$", "metadata"),
				_subscriptionId, 2));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-02", 3, "account-02", 3, false, new TFPos(180, 170), _eventId, "append", false,
					"$", "metadata"),
				_subscriptionId, 3));
	}

	[Test]
	public void request_partition_state_from_the_correct_stream() {
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>()
				.Count(v => v.EventStreamId == "$projections-projection-account-01-checkpoint"));
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>()
				.Count(v => v.EventStreamId == "$projections-projection-account-02-checkpoint"));
	}

	[Test]
	public void update_state_snapshots_are_written_to_the_correct_stream() {
		var writeEvents =
			_writeEventHandler.HandledMessages.Where(v => v.Events.Any(e => e.EventType == "Result")).ToList();
		Assert.AreEqual(4, writeEvents.Count);
		Assert.AreEqual("$projections-projection-account-01-result", writeEvents[0].EventStreamId);
		Assert.AreEqual("$projections-projection-account-02-result", writeEvents[1].EventStreamId);
		Assert.AreEqual("$projections-projection-account-01-result", writeEvents[2].EventStreamId);
		Assert.AreEqual("$projections-projection-account-02-result", writeEvents[3].EventStreamId);
	}

	[Test]
	public void update_state_snapshots_are_correct() {
		var writeEvents =
			_writeEventHandler.HandledMessages.Where(v => v.Events.Any(e => e.EventType == "Result")).ToList();
		Assert.AreEqual(4, writeEvents.Count);
		Assert.AreEqual("data1", Helper.UTF8NoBom.GetString(writeEvents[0].Events[0].Data));
		Assert.AreEqual("data2", Helper.UTF8NoBom.GetString(writeEvents[1].Events[0].Data));
		Assert.AreEqual("data1$", Helper.UTF8NoBom.GetString(writeEvents[2].Events[0].Data));
		Assert.AreEqual("data2$", Helper.UTF8NoBom.GetString(writeEvents[3].Events[0].Data));
	}

	[Test]
	public void register_new_partition_state_stream_only_once() {
		var writes =
			_writeEventHandler.HandledMessages.Where(v => v.EventStreamId == "$projections-projection-partitions")
				.ToArray();
		Assert.AreEqual(2, writes.Length);

		Assert.AreEqual(1, writes[0].Events.Length);
		Assert.AreEqual("account-01", Helper.UTF8NoBom.GetString(writes[0].Events[0].Data));

		Assert.AreEqual(1, writes[1].Events.Length);
		Assert.AreEqual("account-02", Helper.UTF8NoBom.GetString(writes[1].Events[0].Data));
	}
}
