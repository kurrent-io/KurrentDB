// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Tests.Services.core_projection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream.another_epoch;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_handling_emits_with_previously_written_events_at_the_same_position<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private long _1;
	private long _2;
	private long _3;

	protected override void Given() {
		AllWritesQueueUp();
		AllWritesToSucceed("$$test_stream");
		//NOTE: it is possible for a batch of events to be partially written if it contains links
		ExistingEvent("test_stream", "type1", @"{""v"": 1, ""c"": 100, ""p"": 50}", "data");
		ExistingEvent("test_stream", "type2", @"{""v"": 1, ""c"": 100, ""p"": 50}", "data");
		NoOtherStreams();
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
			new ProjectionVersion(1, 2, 2), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 100, 50),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
		_stream.EmitEvents(CreateEventBatch());
		OneWriteCompletes();
	}

	[Test]
	public void truncates_existing_stream_at_correct_position() {
		var writes =
			HandledMessages.OfType<ClientMessage.WriteEvents>()
				.OfEventType(SystemEventTypes.StreamMetadata)
				.ToArray();
		Assert.AreEqual(1, writes.Length);
		HelperExtensions.AssertJson(new {___tb = 2}, writes[0].Data.ParseJson<JObject>());
	}

	[Test]
	public void publishes_all_events() {
		var writtenEvents =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.ExceptOfEventType(SystemEventTypes.StreamMetadata)
				.ToArray();
		Assert.AreEqual(3, writtenEvents.Length);
		Assert.AreEqual("type1", writtenEvents[0].EventType);
		Assert.AreEqual("type2", writtenEvents[1].EventType);
		Assert.AreEqual("type3", writtenEvents[2].EventType);
	}

	[Test]
	public void updates_stream_metadata() {
		var writes =
			HandledMessages.OfType<ClientMessage.WriteEvents>()
				.OfEventType(SystemEventTypes.StreamMetadata)
				.ToArray();
		Assert.AreEqual(1, writes.Length);
	}

	[Test]
	public void reports_correct_event_numbers() {
		Assert.AreEqual(2, _1);
		Assert.AreEqual(3, _2);
		Assert.AreEqual(4, _3);
	}
}
