// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Tests;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.projection_checkpoint;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_stream_awaiting_message<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionCheckpoint _checkpoint;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private FakeEnvelope _fakeEnvelope;

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_checkpoint = new ProjectionCheckpoint(
			_bus, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);

		_fakeEnvelope = new FakeEnvelope();
		_checkpoint.Handle(
			new CoreProjectionProcessingMessage.EmittedStreamAwaiting("awaiting_stream", _fakeEnvelope));
	}

	[Test]
	public void broadcasts_write_completed_to_awaiting_streams() {
		_checkpoint.Handle(new CoreProjectionProcessingMessage.EmittedStreamWriteCompleted("completed_stream"));
		Assert.AreEqual(1, _fakeEnvelope.Replies.Count);
		Assert.IsInstanceOf<CoreProjectionProcessingMessage.EmittedStreamWriteCompleted>(_fakeEnvelope.Replies[0]);
	}

	[Test]
	public void does_not_broadcast_second_write_completed_to_awaiting_streams() {
		_checkpoint.Handle(new CoreProjectionProcessingMessage.EmittedStreamWriteCompleted("completed_stream1"));
		_checkpoint.Handle(new CoreProjectionProcessingMessage.EmittedStreamWriteCompleted("completed_stream2"));
		Assert.AreEqual(1, _fakeEnvelope.Replies.Count);
		Assert.IsInstanceOf<CoreProjectionProcessingMessage.EmittedStreamWriteCompleted>(_fakeEnvelope.Replies[0]);
		Assert.AreEqual("completed_stream1",
			((CoreProjectionProcessingMessage.EmittedStreamWriteCompleted)_fakeEnvelope.Replies[0]).StreamId);
	}
}
