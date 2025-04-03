// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.projection_checkpoint;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_emitting_events_in_correct_order_the_started_projection_checkpoint<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionCheckpoint _checkpoint;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		AllWritesQueueUp();
		AllWritesToSucceed("$$stream1");
		AllWritesToSucceed("$$stream2");
		AllWritesToSucceed("$$stream3");
		NoOtherStreams();
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_checkpoint = new ProjectionCheckpoint(
			_bus, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
		_checkpoint.Start();
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream2", Guid.NewGuid(), "type1", true, "data2", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream3", Guid.NewGuid(), "type2", true, "data3", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream2", Guid.NewGuid(), "type3", true, "data4", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
			});
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream1", Guid.NewGuid(), "type4", true, "data", null,
						CheckpointTag.FromPosition(0, 140, 130), null))
			});
		OneWriteCompletes(); //stream2
		OneWriteCompletes(); //stream3
	}

	[Test]
	public void should_publish_write_events() {
		var writeEvents =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.ExceptOfEventType(SystemEventTypes.StreamMetadata);
		Assert.AreEqual(4, writeEvents.Count());
	}

	[Test]
	public void should_publish_write_events_to_correct_streams() {
		Assert.IsTrue(
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Any(v => v.EventStreamId == "stream1"));
		Assert.IsTrue(
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Any(v => v.EventStreamId == "stream2"));
		Assert.IsTrue(
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Any(v => v.EventStreamId == "stream3"));
	}

	[Test]
	public void should_group_events_to_the_same_stream_caused_by_the_same_event() {
		// this is important for the projection to be able to recover by CausedBy.  Unless we commit all the events
		// to the stream in a single transaction we can get into situation when only part of events CausedBy the same event
		// are present in a stream
		Assert.AreEqual(
			2,
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Single(v => v.EventStreamId == "stream2")
				.Events.Length);
	}

	[Test]
	public void should_not_write_a_second_group_until_the_first_write_completes() {
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream1", Guid.NewGuid(), "type", true, "data", null,
						CheckpointTag.FromPosition(0, 170, 160), null))
			});
		var writeRequests =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Where(v => v.EventStreamId == "stream1");
		var writeEvents = writeRequests.Single();
		writeEvents.Envelope.ReplyWith(
			new ClientMessage.WriteEventsCompleted(writeEvents.CorrelationId, 0, 0, -1, -1));
		Assert.AreEqual(2, writeRequests.Count());
	}
}
