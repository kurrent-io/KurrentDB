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
public class when_handling_an_emit_with_stream_metadata_to_empty_stream<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private EmittedStream.WriterConfiguration.StreamMetadata _streamMetadata;
	private EmittedStream.WriterConfiguration _writerConfiguration;

	protected override void Given() {
		AllWritesQueueUp();
		NoStream("test_stream");
		_streamMetadata = new EmittedStream.WriterConfiguration.StreamMetadata(maxCount: 10);
		_writerConfiguration = new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
			_streamMetadata, null, maxWriteBatchLength: 50);
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_stream = new EmittedStream(
			"test_stream", _writerConfiguration, new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 40, 30), _bus, _ioDispatcher, _readyHandler);
		_stream.Start();
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), null)
			});
	}

	[Test]
	public void publishes_write_stream_metadata() {
		Assert.AreEqual(
			1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().ToStream("$$test_stream").Count());
	}

	[Test]
	public void does_not_write_stream_metadata_second_time() {
		OneWriteCompletes();
		OneWriteCompletes();
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 400, 350), null)
			});
		Assert.AreEqual(
			1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().ToStream("$$test_stream").Count());
	}

	[Test]
	public void publishes_write_emitted_event_on_write_stream_metadata_completed() {
		OneWriteCompletes();
		Assert.AreEqual(
			1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().ToStream("test_stream").Count());
	}

	[Test]
	public void does_not_reply_with_write_completed_message() {
		Assert.AreEqual(0, _readyHandler.HandledWriteCompletedMessage.Count);
	}

	[Test]
	public void reply_with_write_completed_message_when_write_completes() {
		OneWriteCompletes();
		OneWriteCompletes();
		Assert.IsTrue(_readyHandler.HandledWriteCompletedMessage.Any(v => v.StreamId == "test_stream"));
		// more than one is ok
	}
}
