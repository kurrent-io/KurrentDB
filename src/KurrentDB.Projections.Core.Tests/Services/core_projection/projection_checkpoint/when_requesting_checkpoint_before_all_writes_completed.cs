// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.projection_checkpoint;

using ClientMessageWriteEvents = KurrentDB.Core.Tests.TestAdapters.ClientMessage.WriteEvents;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_requesting_checkpoint_before_all_writes_completed<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionCheckpoint _checkpoint;
	private TestCheckpointManagerMessageHandler _readyHandler;

	protected override void Given() {
		AllWritesSucceed();
		NoOtherStreams();
	}

	[SetUp]
	public void setup() {
		_readyHandler = new TestCheckpointManagerMessageHandler();
		_checkpoint = new ProjectionCheckpoint(
			_bus, _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
		_checkpoint.Start();
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream2", Guid.NewGuid(), "type", true, "data2", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream2", Guid.NewGuid(), "type", true, "data4", null,
						CheckpointTag.FromPosition(0, 120, 110), null)),
			});
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream1", Guid.NewGuid(), "type", true, "data", null,
						CheckpointTag.FromPosition(0, 140, 130), null))
			});
		_checkpoint.ValidateOrderAndEmitEvents(
			new[] {
				new EmittedEventEnvelope(
					new EmittedDataEvent(
						"stream1", Guid.NewGuid(), "type", true, "data", null,
						CheckpointTag.FromPosition(0, 160, 150), null))
			});
		_checkpoint.Prepare(CheckpointTag.FromPosition(0, 200, 150));
	}

	[Test]
	public void not_ready_for_checkpoint_immediately() {
		Assert.AreEqual(0,
			_consumer.HandledMessages.OfType<CoreProjectionProcessingMessage.ReadyForCheckpoint>().Count());
	}

	[Test]
	public void ready_for_checkpoint_after_all_writes_complete() {
		var writes = _consumer.HandledMessages.OfType<ClientMessageWriteEvents>().ToArray();
		writes[0].Envelope.ReplyWith(ClientMessage.WriteEventsCompleted.ForSingleStream(writes[0].CorrelationId, 0, 0, -1, -1));
		writes[1].Envelope.ReplyWith(ClientMessage.WriteEventsCompleted.ForSingleStream(writes[1].CorrelationId, 0, 0, -1, -1));
		writes = _consumer.HandledMessages.OfType<ClientMessageWriteEvents>().ToArray();
		writes[2].Envelope.ReplyWith(ClientMessage.WriteEventsCompleted.ForSingleStream(writes[2].CorrelationId, 0, 0, -1, -1));

		Assert.AreEqual(1,
			_readyHandler.HandledMessages.OfType<CoreProjectionProcessingMessage.ReadyForCheckpoint>().Count());
	}
}
