// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
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
public class when_multiple_event_processed_received_the_core_projection_checkpoint_manager<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
	private Exception _exception;

	protected override void Given() {
		AllWritesSucceed();
		base.Given();
		this._checkpointHandledThreshold = 2;
	}

	protected override void When() {
		base.When();
		_exception = null;
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
		} catch (Exception ex) {
			_exception = ex;
		}
	}

	[Test]
	public void messages_are_handled() {
		Assert.IsNull(_exception);
	}

	[Test]
	public void accepts_stopping() {
		_manager.Stopping();
	}

	[Test]
	public void accepts_stopped() {
		_manager.Stopped();
	}

	[Test]
	public void accepts_event_processed() {
//            _manager.StateUpdated("", @"{""state"":""state""}");
		_manager.EventProcessed(CheckpointTag.FromStreamPosition(0, "stream", 13), 77.7f);
	}

	[Test]
	public void event_processed_at_the_start_position_throws_invalid_operation_exception() {
//            _manager.StateUpdated("", @"{""state"":""state""}");
		Assert.Throws<InvalidOperationException>(() => {
			_manager.EventProcessed(CheckpointTag.FromStreamPosition(0, "stream", 10), 77.7f);
		});
	}

	[Test]
	public void accepts_checkpoint_suggested() {
		_manager.CheckpointSuggested(CheckpointTag.FromStreamPosition(0, "stream", 13), 77.7f);
	}

	[Test]
	public void checkpoint_suggested_at_the_start_position_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			_manager.CheckpointSuggested(CheckpointTag.FromStreamPosition(0, "stream", 10), 77.7f);
		});
	}
}
