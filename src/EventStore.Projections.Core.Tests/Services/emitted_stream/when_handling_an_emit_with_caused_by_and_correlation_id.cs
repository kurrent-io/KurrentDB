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

namespace EventStore.Projections.Core.Tests.Services.emitted_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_an_emit_with_caused_by_and_correlation_id<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private EmittedDataEvent _emittedDataEvent;
	private Guid _causedBy;
	private string _correlationId;

	protected override void Given() {
		AllWritesQueueUp();
		AllWritesToSucceed("$$test_stream");
		NoOtherStreams();
	}

	[SetUp]
	public void setup() {
		_causedBy = Guid.NewGuid();
		_correlationId = "correlation_id";

		_emittedDataEvent = new EmittedDataEvent(
			"test_stream", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 200, 150),
			null);

		_emittedDataEvent.SetCausedBy(_causedBy);
		_emittedDataEvent.SetCorrelationId(_correlationId);

		_readyHandler = new TestCheckpointManagerMessageHandler();
		_stream = new EmittedStream(
			"test_stream",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 40, 30),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
		_stream.EmitEvents(new[] {_emittedDataEvent});
	}


	[Test]
	public void publishes_write_events() {
		var writeEvents =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.ExceptOfEventType(SystemEventTypes.StreamMetadata)
				.ToArray();
		Assert.AreEqual(1, writeEvents.Length);
		var writeEvent = writeEvents.Single();
		Assert.NotNull(writeEvent.Metadata);
		var metadata = Helper.UTF8NoBom.GetString(writeEvent.Metadata);
		HelperExtensions.AssertJson(
			new {___causedBy = _causedBy, ___correlationId = _correlationId}, metadata.ParseJson<JObject>());
	}
}
