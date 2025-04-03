// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.stream_position_tagger;

[TestFixture]
public class when_updating_stream_postion_tracker_from_a_tag {
	private StreamPositionTagger _tagger;
	private CheckpointTag _tag;
	private PositionTracker _positionTracker;

	[SetUp]
	public void When() {
		// given
		var tagger = new StreamPositionTagger(0, "stream1");
		var tracker = new PositionTracker(tagger);

		var newTag = CheckpointTag.FromStreamPosition(0, "stream1", 1);
		tracker.UpdateByCheckpointTagInitial(newTag);
		_tag = tracker.LastTag;
		_tagger = new StreamPositionTagger(0, "stream1");
		_positionTracker = new PositionTracker(_tagger);
		// when

		_positionTracker.UpdateByCheckpointTagInitial(_tag);
	}

	[Test]
	public void stream_position_is_updated() {
		Assert.AreEqual(1, _positionTracker.LastTag.Streams["stream1"]);
	}
}
