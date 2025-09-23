// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Services;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TestAdapters;
using KurrentDB.Projections.Core.Messages;
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
public class when_handling_an_emit_with_not_ready_event<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		AllWritesSucceed();
		NoOtherStreams();
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
	public void replies_with_await_message() {
		_stream.EmitEvents(
		[
			new EmittedLinkTo("test_stream", Guid.NewGuid(), "other_stream", CheckpointTag.FromPosition(0, 1100, 1000), null)
		]);
		Assert.AreEqual(1, _readyHandler.HandledStreamAwaitingMessage.Count);
		Assert.AreEqual("test_stream", _readyHandler.HandledStreamAwaitingMessage[0].StreamId);
	}

	[Test]
	public void processes_write_on_write_completed_if_ready() {
		var linkTo = new EmittedLinkTo("test_stream", Guid.NewGuid(), "other_stream", CheckpointTag.FromPosition(0, 1100, 1000), null);
		_stream.EmitEvents([linkTo]);
		linkTo.SetTargetEventNumber(1);
		_stream.Handle(new("other_stream"));

		Assert.AreEqual(1, _readyHandler.HandledStreamAwaitingMessage.Count);
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.OfEventType(SystemEventTypes.LinkTo)
				.Count());
	}

	[Test]
	public void replies_with_await_message_on_write_completed_if_not_yet_ready() {
		var linkTo = new EmittedLinkTo("test_stream", Guid.NewGuid(), "other_stream", CheckpointTag.FromPosition(0, 1100, 1000), null);
		_stream.EmitEvents([linkTo]);
		_stream.Handle(new("one_more_stream"));

		Assert.AreEqual(2, _readyHandler.HandledStreamAwaitingMessage.Count);
		Assert.AreEqual("test_stream", _readyHandler.HandledStreamAwaitingMessage[0].StreamId);
		Assert.AreEqual("test_stream", _readyHandler.HandledStreamAwaitingMessage[1].StreamId);
	}
}
