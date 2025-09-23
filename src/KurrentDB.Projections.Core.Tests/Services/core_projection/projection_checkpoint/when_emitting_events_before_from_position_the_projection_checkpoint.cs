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
public class when_emitting_events_before_from_position_the_projection_checkpoint<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionCheckpoint _checkpoint;
	private Exception _lastException;
	private TestCheckpointManagerMessageHandler _readyHandler;

	[SetUp]
	public void setup() {
		_readyHandler = new();
		_checkpoint = new(_bus, _ioDispatcher, new(1, 0, 0), null, _readyHandler,
			CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250, 1);
		try {
			_checkpoint.ValidateOrderAndEmitEvents(
			[
				new(new EmittedDataEvent("stream1", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 40, 30), null))
			]);
		} catch (Exception ex) {
			_lastException = ex;
		}
	}

	[Test]
	public void throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			if (_lastException != null)
				throw _lastException;
		});
	}
}
