// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.emitted_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_an_emit_with_committed_callback<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		ExistingEvent("test_stream", "type", """{"c": 100, "p": 50}""", "data");
		AllWritesSucceed();
	}

	[SetUp]
	public void setup() {
		_readyHandler = new();
		_stream = new(
			"test_stream",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 0, -1),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
	}

	[Test]
	public void completes_already_published_events() {
		var invoked = false;
		_stream.EmitEvents(
		[
			new EmittedDataEvent("test_stream", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 100, 50), null, _ => invoked = true)
		]);
		Assert.IsTrue(invoked);
	}

	[Test]
	public void completes_not_yet_published_events() {
		var invoked = false;
		_stream.EmitEvents(
		[
			new EmittedDataEvent("test_stream", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 200, 150), null, _ => invoked = true)
		]);
		Assert.IsTrue(invoked);
	}
}
