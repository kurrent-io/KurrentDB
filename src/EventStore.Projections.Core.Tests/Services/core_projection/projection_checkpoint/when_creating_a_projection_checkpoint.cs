// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Fakes;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.projection_checkpoint;

[TestFixture]
public class when_creating_a_projection_checkpoint {
	private FakePublisher _fakePublisher;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private IODispatcher _ioDispatcher;


	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_fakePublisher = new FakePublisher();
		_ioDispatcher = new IODispatcher(_fakePublisher, _fakePublisher, true);
	}

	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new ProjectionCheckpoint(
				null, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
				CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
		});
	}

	[Test]
	public void null_io_dispatcher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new ProjectionCheckpoint(
				_fakePublisher, null, new ProjectionVersion(1, 0, 0), null, _readyHandler,
				CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
		});
	}

	[Test]
	public void null_ready_handler_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new ProjectionCheckpoint(
				_fakePublisher, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, null,
				CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
		});
	}

	[Test]
	public void commit_position_less_than_or_equal_to_prepare_position_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			new ProjectionCheckpoint(
				_fakePublisher, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
				CheckpointTag.FromPosition(0, 100, 101), new TransactionFilePositionTagger(0), 250, 1);
		});
	}

	[Test]
	public void it_can_be_created() {
		new ProjectionCheckpoint(
			_fakePublisher, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
	}
}
