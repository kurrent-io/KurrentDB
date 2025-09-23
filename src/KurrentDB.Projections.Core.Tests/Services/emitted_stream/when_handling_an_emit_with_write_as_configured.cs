// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TestAdapters;
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
public class when_handling_an_emit_with_write_as_configured<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private ClaimsPrincipal _writeAs;

	protected override void Given() {
		AllWritesQueueUp();
		ExistingEvent("test_stream", "type", """{"c": 100, "p": 50}""", "data");
	}

	[SetUp]
	public void setup() {
		_readyHandler = new();
		_writeAs = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "test-user")], "ES-Test"));
		_stream = new(
			"test_stream",
			new(new EmittedStreamsWriter(_ioDispatcher), new(), _writeAs, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 40, 30),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();

		_stream.EmitEvents(
		[
			new EmittedDataEvent("test_stream", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 200, 150), null)
		]);
	}

	[Test]
	public void publishes_not_yet_published_events() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
	}

	[Test]
	public void publishes_write_event_with_correct_user_account() {
		var writeEvent = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Single();

		Assert.AreSame(_writeAs, writeEvent.User);
	}
}
