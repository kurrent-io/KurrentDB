// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.projection_checkpoint;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class the_started_checkpoint_with_some_events_emitted<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionCheckpoint _checkpoint;
	private TestCheckpointManagerMessageHandler _readyHandler;

	[SetUp]
	public void setup() {
		_readyHandler = new();
		_checkpoint = new(_bus, _ioDispatcher, new(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
		_checkpoint.Start();
		_checkpoint.ValidateOrderAndEmitEvents(
		[
			new(new EmittedDataEvent("stream2", Guid.NewGuid(), "type", true, "data2", null, CheckpointTag.FromPosition(0, 120, 110), null)),
			new(new EmittedDataEvent("stream2", Guid.NewGuid(), "type", true, "data4", null, CheckpointTag.FromPosition(0, 120, 110), null))
		]);
		_checkpoint.ValidateOrderAndEmitEvents(
		[
			new(new EmittedDataEvent("stream1", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 140, 130), null))
		]);
		_checkpoint.ValidateOrderAndEmitEvents(
		[
			new(new EmittedDataEvent("stream1", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 160, 150), null))
		]);
	}

	[Test]
	public void requesting_checkpoints_with_position_before_the_last_known_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			_checkpoint.Prepare(CheckpointTag.FromPosition(0, 140, 130));
		});
	}
}
