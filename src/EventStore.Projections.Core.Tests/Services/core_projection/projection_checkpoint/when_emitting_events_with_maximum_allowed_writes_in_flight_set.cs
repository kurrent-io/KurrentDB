// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using System.Collections;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Common;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Tests.Services.core_projection.projection_checkpoint;


[TestFixture(typeof(LogFormat.V2), typeof(string), 1)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), 1)]
[TestFixture(typeof(LogFormat.V2), typeof(string), 2)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), 2)]
[TestFixture(typeof(LogFormat.V2), typeof(string), 3)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), 3)]
public class when_emitting_events_with_maximum_allowed_writes_in_flight_set<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionCheckpoint _checkpoint;
	private TestCheckpointManagerMessageHandler _readyHandler;

	private int _maximumNumberOfAllowedWritesInFlight;

	public when_emitting_events_with_maximum_allowed_writes_in_flight_set(
		int maximumNumberOfAllowedWritesInFlight) {
		_maximumNumberOfAllowedWritesInFlight = maximumNumberOfAllowedWritesInFlight;
	}

	protected override void Given() {
		AllWritesQueueUp();
		AllWritesToSucceed("$$stream1");
		AllWritesToSucceed("$$stream2");
		AllWritesToSucceed("$$stream3");
		NoOtherStreams();
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_checkpoint = new ProjectionCheckpoint(
			_bus, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250,
			_maximumNumberOfAllowedWritesInFlight);
		_checkpoint.Start();
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream1", Guid.NewGuid(), "type1", true, "data1", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream2", Guid.NewGuid(), "type2", true, "data2", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream3", Guid.NewGuid(), "type3", true, "data3", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
			});
	}

	[Test]
	public void should_have_the_same_number_writes_in_flight_as_configured() {
		var writeEvents =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.ExceptOfEventType(SystemEventTypes.StreamMetadata);
		Assert.AreEqual(_maximumNumberOfAllowedWritesInFlight, writeEvents.Count());
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_emitting_events_with_maximum_allowed_writes_in_flight_set_to_unlimited<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionCheckpoint _checkpoint;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		AllWritesQueueUp();
		AllWritesToSucceed("$$stream1");
		AllWritesToSucceed("$$stream2");
		AllWritesToSucceed("$$stream3");
		NoOtherStreams();
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_checkpoint = new ProjectionCheckpoint(
			_bus, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250,
			AllowedWritesInFlight.Unbounded);
		_checkpoint.Start();
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream1", Guid.NewGuid(), "type1", true, "data1", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream2", Guid.NewGuid(), "type2", true, "data2", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream3", Guid.NewGuid(), "type3", true, "data3", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
			});
	}

	[Test]
	public void should_have_as_many_writes_in_flight_as_requested() {
		var writeEvents =
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.ExceptOfEventType(SystemEventTypes.StreamMetadata);
		Assert.AreEqual(3, writeEvents.Count());
	}
}
