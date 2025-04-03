// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.stream_position_tagger;

[TestFixture]
public class when_updating_postion_stream_position_tracker {
	private StreamPositionTagger _tagger;
	private PositionTracker _positionTracker;

	[SetUp]
	public void When() {
		// given
		_tagger = new StreamPositionTagger(0, "stream1");
		_positionTracker = new PositionTracker(_tagger);
		var newTag = CheckpointTag.FromStreamPosition(0, "stream1", 1);
		var newTag2 = CheckpointTag.FromStreamPosition(0, "stream1", 2);
		_positionTracker.UpdateByCheckpointTagInitial(newTag);
		_positionTracker.UpdateByCheckpointTagForward(newTag2);
	}

	[Test]
	public void stream_position_is_updated() {
		Assert.AreEqual(2, _positionTracker.LastTag.Streams["stream1"]);
	}


	[Test]
	public void cannot_update_to_the_same_postion() {
		Assert.Throws<InvalidOperationException>(() => {
			var newTag = CheckpointTag.FromStreamPosition(0, "stream1", 2);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
		});
	}

	[Test]
	public void it_cannot_be_updated_with_other_stream() {
		Assert.Throws<InvalidOperationException>(() => {
			// even not initialized (UpdateToZero can be removed)
			var newTag = CheckpointTag.FromStreamPosition(0, "other_stream1", 2);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
		});
	}

	//TODO: write tests on updating with incompatible snapshot loaded
}
