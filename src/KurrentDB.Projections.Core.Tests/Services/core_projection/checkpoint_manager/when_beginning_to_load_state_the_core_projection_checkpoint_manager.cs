// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_beginning_to_load_state_the_core_projection_checkpoint_manager<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
	private Exception _exception;

	protected override void When() {
		base.When();
		_exception = null;
		try {
			_checkpointReader.BeginLoadState();
		} catch (Exception ex) {
			_exception = ex;
		}
	}

	[Test]
	public void it_can_be_invoked() {
		Assert.IsNull(_exception);
	}

	[Test]
	public void start_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => { _checkpointReader.BeginLoadState(); });
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
	public void can_be_started() {
		_manager.Start(CheckpointTag.FromStreamPosition(0, "stream", 10), null);
	}

	[Test]
	public void cannot_be_started_from_incompatible_checkpoint_tag() {
		//TODO: move to when loaded
		Assert.Throws<InvalidOperationException>(() => {
			_manager.Start(CheckpointTag.FromStreamPosition(0, "stream1", 10), null);
		});
	}
}
