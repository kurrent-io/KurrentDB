// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream;

[TestFixture]
public class when_checkpoint_requested : TestFixtureWithReadWriteDispatchers {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		;
		_stream = new EmittedStream(
			"test",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50), new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _bus, _ioDispatcher,
			_readyHandler);
		_stream.Start();
		_stream.Checkpoint();
	}

	[Test]
	public void emit_events_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			_stream.EmitEvents(
				new[] {
					new EmittedDataEvent(
						"test", Guid.NewGuid(), "type2", true, "data2", null, CheckpointTag.FromPosition(0, -1, -1),
						null)
				});
		});
	}

	[Test]
	public void checkpoint_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => { _stream.Checkpoint(); });
	}
}
