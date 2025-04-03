// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.projection_checkpoint;

[TestFixture]
public class when_the_projection_checkpoint_has_been_started : TestFixtureWithReadWriteDispatchers {
	private ProjectionCheckpoint _checkpoint;
	private TestCheckpointManagerMessageHandler _readyHandler;

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_checkpoint = new ProjectionCheckpoint(
			_bus, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 0, -1), new TransactionFilePositionTagger(0), 250, 1);
		_checkpoint.Start();
	}

	[Test]
	public void start_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => { _checkpoint.Start(); });
	}
}
