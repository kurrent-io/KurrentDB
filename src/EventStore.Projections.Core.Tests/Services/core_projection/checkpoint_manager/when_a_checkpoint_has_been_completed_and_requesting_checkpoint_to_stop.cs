// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_checkpoint_has_been_completed_and_requesting_checkpoint_to_stop<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
	private Exception _exception;

	protected override void Given() {
		AllWritesSucceed();
		base.Given();
		this._checkpointHandledThreshold = 2; //NOTE: does not play any role anymore here
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
			_manager.EventProcessed(CheckpointTag.FromStreamPosition(0, "stream", 12), 77.8f);
			_manager.CheckpointSuggested(CheckpointTag.FromStreamPosition(0, "stream", 12), 77.8f);
			_manager.Stopping();
		} catch (Exception ex) {
			_exception = ex;
		}
	}

	[Test]
	public void does_not_throw() {
		Assert.IsNull(_exception);
	}

	[Test]
	public void two_checkpoints_are_completed() {
		Assert.AreEqual(2, _projection._checkpointCompletedMessages.Count);
	}


	[Test]
	public void only_one_checkpoint_has_been_written() {
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
				.ToStream("$projections-projection-checkpoint")
				.Count());
	}
}
