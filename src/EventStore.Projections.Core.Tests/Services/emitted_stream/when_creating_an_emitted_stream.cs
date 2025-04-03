// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Helpers;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Fakes;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream;

[TestFixture]
public class when_creating_an_emitted_stream {
	private FakePublisher _fakePublisher;
	private IODispatcher _ioDispatcher;


	[SetUp]
	public void setup() {
		_fakePublisher = new FakePublisher();
		_ioDispatcher = new IODispatcher(_fakePublisher, _fakePublisher, true);
	}

	[Test]
	public void null_stream_id_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new EmittedStream(
				null,
				new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
					new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50),
				new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
				_ioDispatcher,
				new TestCheckpointManagerMessageHandler());
		});
	}

	[Test]
	public void null_writer_configuration_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new EmittedStream(
				null, null, new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
				_ioDispatcher,
				new TestCheckpointManagerMessageHandler());
		});
	}

	[Test]
	public void empty_stream_id_throws_argument_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new EmittedStream(
				"",
				new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
					new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50),
				new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
				_ioDispatcher,
				new TestCheckpointManagerMessageHandler());
		});
	}

	[Test]
	public void null_from_throws_argument_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new EmittedStream(
				"",
				new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
					new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50),
				new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), null, _fakePublisher, _ioDispatcher,
				new TestCheckpointManagerMessageHandler());
		});
	}

	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new EmittedStream(
				"test",
				new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
					new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50),
				new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), null, _ioDispatcher,
				new TestCheckpointManagerMessageHandler());
		});
	}

	[Test]
	public void null_io_dispatcher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new EmittedStream(
				"test",
				new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
					new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50),
				new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher, null,
				new TestCheckpointManagerMessageHandler());
		});
	}

	[Test]
	public void null_ready_handler_throws_argumenbt_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new EmittedStream(
				"test",
				new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
					new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50),
				new ProjectionVersion(1, 0, 0),
				new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
				_ioDispatcher, null);
		});
	}

	[Test]
	public void it_can_be_created() {
		new EmittedStream(
			"test",
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, 50), new ProjectionVersion(1, 0, 0),
			new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 0, -1), _fakePublisher,
			_ioDispatcher,
			new TestCheckpointManagerMessageHandler());
	}
}
