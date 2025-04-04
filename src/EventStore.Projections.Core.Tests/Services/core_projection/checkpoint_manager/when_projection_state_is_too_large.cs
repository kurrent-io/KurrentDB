// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_projection_state_is_too_large<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
	private Exception _exception;

	protected override void Given() {
		AllWritesSucceed();
		base.Given();
		this._checkpointHandledThreshold = 2;
		this._maxProjectionStateSize = 1024 * 1024;
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

			// Initial checkpoint and state
			var initialCheckpointTag = CheckpointTag.FromStreamPosition(0, "stream", 10);
			_manager.Start(initialCheckpointTag, null);
			var oldState = new PartitionState("", "", initialCheckpointTag);

			// checkpoint and state after first event processed
			var firstEventCheckpointTag = CheckpointTag.FromStreamPosition(0, "stream", 11);
			var newState = new PartitionState("{ \"state\": \"foo\"}", "",
				firstEventCheckpointTag);
			_manager.StateUpdated("", oldState, newState);
			_manager.EventProcessed(firstEventCheckpointTag, 55.5f);

			// second event processed fails, as the state is too large
			var secondEventCheckpointTag = CheckpointTag.FromStreamPosition(0, "stream", 12);
			oldState = newState;
			newState = new PartitionState($"{{ \"state\": \"{new string('*', _maxProjectionStateSize)}\"}}", "",
				firstEventCheckpointTag);
			_manager.StateUpdated("", oldState, newState);
			_manager.EventProcessed(secondEventCheckpointTag, 77.7f);
		} catch (Exception ex) {
			_exception = ex;
		}
	}

	[Test]
	public void messages_are_handled() {
		Assert.IsNull(_exception);
	}

	[Test]
	public void publishes_projection_failed_message() {
		var failedMessages = _consumer.HandledMessages.OfType<CoreProjectionProcessingMessage.Failed>().ToArray();
		Assert.AreEqual(1, failedMessages.Length);
		Assert.True(failedMessages[0].Reason.Contains("exceeds the configured MaxProjectionStateSize"));
	}

	[Test]
	public void the_second_event_is_not_processed() {
		var stats = new ProjectionStatistics();
		_manager.GetStatistics(stats);
		Assert.AreEqual(1, stats.EventsProcessedAfterRestart);
		Assert.AreEqual(55.5f, stats.Progress);
		Assert.AreEqual(CheckpointTag.FromStreamPosition(0, "stream", 11).ToString(), stats.Position);
	}
}
