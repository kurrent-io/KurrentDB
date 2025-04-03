// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.stream_position_tagger;

[TestFixture]
public class when_creating_stream_postion_tracker {
	private StreamPositionTagger _tagger;
	private PositionTracker _positionTracker;

	[SetUp]
	public void when() {
		_tagger = new StreamPositionTagger(0, "stream1");
		_positionTracker = new PositionTracker(_tagger);
	}

	[Test]
	public void it_can_be_updated_with_correct_stream() {
		// even not initialized (UpdateToZero can be removed)
		var newTag = CheckpointTag.FromStreamPosition(0, "stream1", 1);
		_positionTracker.UpdateByCheckpointTagInitial(newTag);
	}

	[Test]
	public void it_cannot_be_updated_with_other_stream() {
		Assert.Throws<InvalidOperationException>(() => {
			var newTag = CheckpointTag.FromStreamPosition(0, "other_stream1", 1);
			_positionTracker.UpdateByCheckpointTagInitial(newTag);
		});
	}

	[Test]
	public void it_cannot_be_updated_forward() {
		Assert.Throws<InvalidOperationException>(() => {
			var newTag = CheckpointTag.FromStreamPosition(0, "stream1", 1);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
		});
	}

	[Test]
	public void initial_position_cannot_be_set_twice() {
		Assert.Throws<InvalidOperationException>(() => {
			var newTag = CheckpointTag.FromStreamPosition(0, "stream1", 1);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
		});
	}

	[Test]
	public void it_can_be_updated_to_zero() {
		_positionTracker.UpdateByCheckpointTagInitial(_tagger.MakeZeroCheckpointTag());
	}
}
