// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
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
public class when_a_read_completes_before_a_timeout_in_recovery<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private const string TestStreamId = "test_stream";
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private readonly List<TimerMessage.Schedule> _timerMessages = [];

	protected override void Given() {
		AllWritesQueueUp();
		ExistingEvent(TestStreamId, "type", """{"c": 100, "p": 50}""", "data");
		ReadsBackwardQueuesUp();
	}

	[SetUp]
	public void setup() {
		_readyHandler = new();
		_bus.Subscribe(new AdHocHandler<TimerMessage.Schedule>(msg => _timerMessages.Add(msg)));

		_stream = new(
			TestStreamId,
			new(new EmittedStreamsWriter(_ioDispatcher), new(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 40, 30),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
		_stream.EmitEvents(
		[
			new EmittedDataEvent(TestStreamId, Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 200, 150), null)
		]);
		CompleteOneReadBackwards();
	}

	[Test]
	public void should_not_retry_the_read_upon_the_read_timing_out() {
		var timerMessage = _timerMessages.FirstOrDefault();
		Assert.NotNull(timerMessage, $"Expected a {nameof(TimerMessage.Schedule)} to have been published, but none were received");
		timerMessage.Reply();

		var readEventsBackwards = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>().Where(x => x.EventStreamId == TestStreamId);

		Assert.AreEqual(1, readEventsBackwards.Count());
	}
}
