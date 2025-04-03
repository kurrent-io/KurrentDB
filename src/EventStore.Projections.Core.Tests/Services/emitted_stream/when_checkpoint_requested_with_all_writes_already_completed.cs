// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Helpers;
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
public class when_checkpoint_requested_with_all_writes_already_completed<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		base.Given();
		AllWritesSucceed();
		NoOtherStreams();
	}

	[SetUp]
	public void Setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_stream = new EmittedStream(
			"test",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50), new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _bus, _ioDispatcher,
			_readyHandler);
		_stream.Start();
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					"test", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 10, 5), null)
			});
		var msg = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().First();
		_bus.Publish(new ClientMessage.WriteEventsCompleted(msg.CorrelationId, 0, 0, -1, -1));
		_stream.Checkpoint();
	}

	[Test]
	public void publishes_ready_for_checkpoint() {
		Assert.IsTrue(
			_readyHandler.HandledMessages.ContainsSingle<CoreProjectionProcessingMessage.ReadyForCheckpoint>());
	}
}
