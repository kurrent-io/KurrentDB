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
public class
	when_handling_emits_with_previously_written_events_at_the_same_position<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private long _1;
	private long _2;
	private long _3;

	protected override void Given() {
		AllWritesQueueUp();
		//NOTE: it is possible for a batch of events to be partially written if it contains links
		ExistingEvent("test_stream", "type1", @"{""c"": 100, ""p"": 50}", "data");
		ExistingEvent("test_stream", "type2", @"{""c"": 100, ""p"": 50}", "data");
	}

	private EmittedEvent[] CreateEventBatch() {
		return new EmittedEvent[] {
			new EmittedDataEvent(
				(string)"test_stream", Guid.NewGuid(), (string)"type1", (bool)true,
				(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 100, 50), (CheckpointTag)null,
				v => _1 = v),
			new EmittedDataEvent(
				(string)"test_stream", Guid.NewGuid(), (string)"type2", (bool)true,
				(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 100, 50), (CheckpointTag)null,
				v => _2 = v),
			new EmittedDataEvent(
				(string)"test_stream", Guid.NewGuid(), (string)"type3", (bool)true,
				(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 100, 50), (CheckpointTag)null,
				v => _3 = v)
		};
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_stream = new EmittedStream(
			"test_stream",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 20, 10),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
		_stream.EmitEvents(CreateEventBatch());
		OneWriteCompletes();
	}

	[Test]
	public void publishes_not_yet_written_events_only() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
		var writeMessage = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Single();
		Assert.AreEqual(1, writeMessage.Events.Length);
		Assert.AreEqual("type3", writeMessage.Events[0].EventType);
	}

	[Test]
	public void reports_correct_event_numbers() {
		Assert.AreEqual(0, _1);
		Assert.AreEqual(1, _2);
		Assert.AreEqual(2, _3);
	}
}
