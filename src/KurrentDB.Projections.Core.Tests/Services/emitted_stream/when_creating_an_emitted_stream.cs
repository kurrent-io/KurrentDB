// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.emitted_stream;

[TestFixture]
public class when_creating_an_emitted_stream {
	private FakePublisher _fakePublisher;
	private IODispatcher _ioDispatcher;

	[SetUp]
	public void setup() {
		_fakePublisher = new();
		_ioDispatcher = new(_fakePublisher, _fakePublisher, true);
	}

	[Test]
	public void null_stream_id_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => _ = new EmittedStream(
			null,
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, 50),
			new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
			_ioDispatcher,
			new TestCheckpointManagerMessageHandler()));
	}

	[Test]
	public void null_writer_configuration_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => _ = new EmittedStream(
			null, null, new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
			_ioDispatcher,
			new TestCheckpointManagerMessageHandler()));
	}

	[Test]
	public void empty_stream_id_throws_argument_exception() {
		Assert.Throws<ArgumentNullException>(() => _ = new EmittedStream(
			"",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, 50),
			new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
			_ioDispatcher,
			new TestCheckpointManagerMessageHandler()));
	}

	[Test]
	public void null_from_throws_argument_exception() {
		Assert.Throws<ArgumentNullException>(() => _ = new EmittedStream(
			"",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, 50),
			new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), null, _fakePublisher, _ioDispatcher,
			new TestCheckpointManagerMessageHandler()));
	}

	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => _ = new EmittedStream(
			"test",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, 50),
			new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), null, _ioDispatcher,
			new TestCheckpointManagerMessageHandler()));
	}

	[Test]
	public void null_io_dispatcher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => _ = new EmittedStream(
			"test",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, 50),
			new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher, null,
			new TestCheckpointManagerMessageHandler()));
	}

	[Test]
	public void null_ready_handler_throws_argumenbt_null_exception() {
		Assert.Throws<ArgumentNullException>(() => _ = new EmittedStream(
			"test",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, 50),
			new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
			_ioDispatcher, null));
	}

	[Test]
	public void it_can_be_created() {
		_ = new EmittedStream(
			"test",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, 50), new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
			_ioDispatcher,
			new TestCheckpointManagerMessageHandler());
	}
}
