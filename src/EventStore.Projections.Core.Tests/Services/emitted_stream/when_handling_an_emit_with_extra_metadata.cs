// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
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

namespace EventStore.Projections.Core.Tests.Services.emitted_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_an_emit_with_extra_metadata<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		AllWritesQueueUp();
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
			CheckpointTag.FromPosition(0, 40, 30),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();

		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test_stream", Guid.NewGuid(), "type", true, "data",
					new ExtraMetaData(new Dictionary<string, string> {{"a", "1"}, {"b", "{}"}}),
					CheckpointTag.FromPosition(0, 200, 150), null)
			});
	}

	[Test]
	public void publishes_not_yet_published_events() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void combines_checkpoint_tag_with_extra_metadata() {
		var writeEvent = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Single();

		Assert.AreEqual(1, writeEvent.Events.Length);
		var @event = writeEvent.Events[0];
		var metadata = Helper.UTF8NoBom.GetString(@event.Metadata).ParseJson<JObject>();

		HelperExtensions.AssertJson(new {a = 1, b = new { }}, metadata);
		var checkpoint = @event.Metadata.ParseCheckpointTagJson();
		Assert.AreEqual(CheckpointTag.FromPosition(0, 200, 150), checkpoint);
	}
}
