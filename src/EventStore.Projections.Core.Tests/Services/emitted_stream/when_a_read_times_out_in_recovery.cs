// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Tests.Services.emitted_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_read_times_out_in_recovery<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private const string TestStreamId = "test_stream";
	private EmittedStream _stream;
	private TestCheckpointManagerMessageHandler _readyHandler;
	private List<TimerMessage.Schedule> timerMessages = new();

	protected override void Given() {
		AllWritesQueueUp();
		ExistingEvent(TestStreamId, "type", @"{""c"": 100, ""p"": 50}", "data");
		ReadsBackwardQueuesUp();
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_bus.Subscribe(new AdHocHandler<TimerMessage.Schedule>(msg => timerMessages.Add(msg)));

		_stream = new EmittedStream(
			TestStreamId,
			new EmittedStream.WriterConfiguration(new EmittedStreamsWriter(_ioDispatcher),
				new EmittedStream.WriterConfiguration.StreamMetadata(), null, maxWriteBatchLength: 50),
			new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0),
			CheckpointTag.FromPosition(0, 40, 30),
			_bus, _ioDispatcher, _readyHandler);
		_stream.Start();
		_stream.EmitEvents(
			new[] {
				new EmittedDataEvent(
					TestStreamId, Guid.NewGuid(), "type", true, "data", null,
					CheckpointTag.FromPosition(0, 200, 150), null)
			});
	}

	[Test]
	public void should_retry_the_read_upon_the_read_timing_out() {
		var timerMessage = timerMessages.FirstOrDefault();
		Assert.NotNull(timerMessage,
			$"Expected a {nameof(TimerMessage.Schedule)} to have been published, but none were received");

		timerMessage.Reply();

		var readEventsBackwards = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>()
						.Where(x => x.EventStreamId == TestStreamId).ToArray();
		Assert.AreEqual(2, readEventsBackwards.Length);
		Assert.AreEqual(readEventsBackwards[0].FromEventNumber, readEventsBackwards[1].FromEventNumber);
	}
}
