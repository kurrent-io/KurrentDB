// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
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
public class when_handling_an_emit_with_committed_callback<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		ExistingEvent("test_stream", "type", @"{""c"": 100, ""p"": 50}", "data");
		AllWritesSucceed();
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
	public void completes_already_published_events() {
		var invoked = false;
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					(string)"test_stream", Guid.NewGuid(), (string)"type", (bool)true,
					(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 100, 50),
					(CheckpointTag)null, v => invoked = true)
			});
		Assert.IsTrue(invoked);
	}

	[Test]
	public void completes_not_yet_published_events() {
		var invoked = false;
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					(string)"test_stream", Guid.NewGuid(), (string)"type", (bool)true,
					(string)"data", (ExtraMetaData)null, CheckpointTag.FromPosition(0, 200, 150),
					(CheckpointTag)null, v => invoked = true)
			});
		Assert.IsTrue(invoked);
	}
}
