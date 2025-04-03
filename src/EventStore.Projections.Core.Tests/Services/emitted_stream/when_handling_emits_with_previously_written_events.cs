// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_emits_with_previously_written_events<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		AllWritesQueueUp();
		ExistingEvent("test_stream", "type1", @"{""c"": 100, ""p"": 50}", "data");
		ExistingEvent("test_stream", "type2", @"{""c"": 200, ""p"": 150}", "data");
		ExistingEvent("test_stream", "type3", @"{""c"": 300, ""p"": 250}", "data");
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_stream = new EmittedStream(
			"test_stream",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 100, 50), _bus, _ioDispatcher, _readyHandler);
		_stream.Start();
	}

	[Test]
	public void does_not_publish_already_published_events() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type2", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), null)
			});
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type3", true, "data", null,
					CheckpointTag.FromPosition(0, 300, 250), null)
			});
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void does_not_fail_the_projection_if_events_are_skipped() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type3", true, "data", null,
					CheckpointTag.FromPosition(0, 300, 250), null)
			});
		Assert.AreEqual(0, _readyHandler.HandledFailedMessages.Count);
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void fails_the_projection_if_events_are_at_different_positions() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type3", true, "data", null,
					CheckpointTag.FromPosition(0, 250, 220), null)
			});
		Assert.AreEqual(1, _readyHandler.HandledFailedMessages.Count);
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void publishes_not_yet_published_events() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 400, 350), null)
			});
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void replies_with_write_completed_message_for_existing_events() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type2", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), null)
			});
		Assert.AreEqual(1, _readyHandler.HandledWriteCompletedMessage.Count);
	}

	[Test]
	public void retrieves_event_number_for_previously_written_events() {
		long eventNumber = -1;
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					(string)"test_stream", Guid.NewGuid(), (string)"type2", (bool)true,
					(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 200, 150),
					(CheckpointTag)null, v => eventNumber = v)
			});
		Assert.AreEqual(1, eventNumber);
	}

	[Test]
	public void reply_with_write_completed_message_when_write_completes() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 400, 350), null)
			});
		OneWriteCompletes();
		Assert.IsTrue(_readyHandler.HandledWriteCompletedMessage.Any(v => v.StreamId == "test_stream"));
		// more than one is ok
	}

	[Test]
	public void reports_event_number_for_new_events() {
		long eventNumber = -1;
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					(string)"test_stream", Guid.NewGuid(), (string)"type", (bool)true,
					(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 400, 350),
					(CheckpointTag)null, v => eventNumber = v)
			});
		OneWriteCompletes();
		Assert.AreEqual(3, eventNumber);
	}

	[Test]
	public void does_not_fail_if_link_event_target_does_not_exist() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "$>", true, "0@foobar", null,
					CheckpointTag.FromPosition(0, 150, 100), null)
			});
		Assert.AreEqual(0, _readyHandler.HandledFailedMessages.Count);
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.ReadEvent>().Count());
	}
}
