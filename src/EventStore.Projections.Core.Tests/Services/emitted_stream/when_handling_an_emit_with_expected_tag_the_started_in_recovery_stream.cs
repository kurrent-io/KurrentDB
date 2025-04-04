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
	when_handling_an_emit_with_expected_tag_the_started_in_recovery_stream<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		ExistingEvent("test_stream", "type", @"{""c"": 100, ""p"": 50}", "data");
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_stream = new EmittedStream(
			"test_stream",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 0, -1),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
	}

	[Test]
	public void does_not_publish_already_published_events() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 100, 50), CheckpointTag.FromPosition(0, 40, 20))
			});
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void publishes_not_yet_published_events_if_expected_tag_is_the_same() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), CheckpointTag.FromPosition(0, 100, 50))
			});
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void does_not_publish_not_yet_published_events_if_expected_tag_is_before_last_event_tag() {
		//TODO: is it corrupted dB case? 
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), CheckpointTag.FromPosition(0, 40, 20))
			});
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void correct_stream_id_is_set_on_write_events_message() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), CheckpointTag.FromPosition(0, 100, 50))
			});
		Assert.AreEqual(
			"test_stream", _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Single().EventStreamId);
	}

	[Test]
	public void metadata_include_commit_and_prepare_positions() {
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), CheckpointTag.FromPosition(0, 100, 50))
			});
		var metaData =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Single().Events[0].Metadata
				.ParseCheckpointTagVersionExtraJson(default(ProjectionVersion));
		Assert.AreEqual(200, metaData.Tag.CommitPosition);
		Assert.AreEqual(150, metaData.Tag.PreparePosition);
	}
}
