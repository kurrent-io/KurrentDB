// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_default_checkpoint_manager_has_been_reinitialized<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
	//private Exception _exception;

	protected override void Given() {
		AllWritesSucceed();
		base.Given();
		_checkpointHandledThreshold = 2;
	}

	protected override void When() {
		base.When();
		//_exception = null;
		try {
			_checkpointReader.BeginLoadState();
			var checkpointLoaded =
				_consumer.HandledMessages.OfType<CoreProjectionProcessingMessage.CheckpointLoaded>().First();
			_checkpointWriter.StartFrom(checkpointLoaded.CheckpointTag, checkpointLoaded.CheckpointEventNumber);
			_manager.BeginLoadPrerecordedEvents(checkpointLoaded.CheckpointTag);

			_manager.Start(CheckpointTag.FromStreamPosition(0, "stream", 10), null);
//                _manager.StateUpdated("", @"{""state"":""state1""}");
			_manager.EventProcessed(CheckpointTag.FromStreamPosition(0, "stream", 11), 77.7f);
//                _manager.StateUpdated("", @"{""state"":""state2""}");
			_manager.EventProcessed(CheckpointTag.FromStreamPosition(0, "stream", 12), 77.7f);
			_manager.Initialize();
			_checkpointReader.Initialize();
		} catch (Exception) {
			//_exception = ex;
		}
	}


	[Test]
	public void stopping_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => { _manager.Stopping(); });
	}

	[Test]
	public void stopped_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => { _manager.Stopped(); });
	}

	[Test]
	public void event_processed_throws_invalid_operation_exception() {
//            _manager.StateUpdated("", @"{""state"":""state""}");
		Assert.Throws<InvalidOperationException>(() => {
			_manager.EventProcessed(CheckpointTag.FromStreamPosition(0, "stream", 10), 77.7f);
		});
	}

	[Test]
	public void checkpoint_suggested_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			_manager.CheckpointSuggested(CheckpointTag.FromStreamPosition(0, "stream", 10), 77.7f);
		});
	}

	[Test]
	public void ready_for_checkpoint_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			_manager.Handle(new CoreProjectionProcessingMessage.ReadyForCheckpoint(null));
		});
	}

	[Test]
	public void can_begin_load_state() {
		_checkpointReader.BeginLoadState();
	}

	[Test]
	public void can_be_started() {
		_manager.Start(CheckpointTag.FromStreamPosition(0, "stream", 10), null);
	}
}
