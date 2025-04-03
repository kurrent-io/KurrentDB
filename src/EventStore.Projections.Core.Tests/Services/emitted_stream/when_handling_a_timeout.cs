// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using System.Collections.Generic;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream.another_epoch;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_a_timeout<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		AllWritesQueueUp();
		ExistingEvent("test_stream", "type1", @"{""v"": 1, ""c"": 100, ""p"": 50}", "data");
		ExistingEvent("test_stream", "type1", @"{""v"": 2, ""c"": 100, ""p"": 50}", "data");
	}

	private EmittedEvent[] CreateEventBatch() {
		return new EmittedEvent[] {
			new EmittedDataEvent(
				(string)"test_stream", Guid.NewGuid(), (string)"type1", (bool)true,
				(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 100, 50), (CheckpointTag)null,
				null),
			new EmittedDataEvent(
				(string)"test_stream", Guid.NewGuid(), (string)"type2", (bool)true,
				(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 100, 50), (CheckpointTag)null,
				null),
			new EmittedDataEvent(
				(string)"test_stream", Guid.NewGuid(), (string)"type3", (bool)true,
				(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 100, 50), (CheckpointTag)null,
				null)
		};
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_stream = new EmittedStream(
			"test_stream",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 2, 2), new TransactionFilePositionTagger(0), CheckpointTag.Empty,
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
		_stream.EmitEvents(CreateEventBatch());

		CompleteWriteWithResult(OperationResult.CommitTimeout);
	}

	[Test]
	public void should_retry_the_write_with_the_same_events() {
		var current = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Last();
		while (_consumer.HandledMessages.Last().GetType() ==
		       typeof(EventStore.Core.Services.TimerService.TimerMessage.Schedule)) {
			var message =
				_consumer.HandledMessages.Last() as EventStore.Core.Services.TimerService.TimerMessage.Schedule;
			message.Envelope.ReplyWith(message.ReplyMessage);

			CompleteWriteWithResult(OperationResult.CommitTimeout);

			var last = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Last();

			Assert.AreEqual(current.EventStreamId, last.EventStreamId);
			Assert.AreEqual(current.Events, last.Events);

			current = last;
		}

		Assert.AreEqual(1,
			_readyHandler.HandledFailedMessages.OfType<CoreProjectionProcessingMessage.Failed>().Count(),
			"Should fail the projection after exhausting all the write retries");
	}
}
