// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.multistream_position_tagger;

[TestFixture]
public class when_updating_multistream_postion_tracker_from_a_tag {
	private MultiStreamPositionTagger _tagger;
	private CheckpointTag _tag;
	private PositionTracker _positionTracker;

	[SetUp]
	public void When() {
		// given
		var tagger = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		var tracker = new PositionTracker(tagger);

		var newTag =
			CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"stream1", 1}, {"stream2", 2}});
		tracker.UpdateByCheckpointTagInitial(newTag);
		_tag = tracker.LastTag;
		_tagger = new MultiStreamPositionTagger(0, new[] {"stream1", "stream2"});
		_positionTracker = new PositionTracker(_tagger);
		// when

		_positionTracker.UpdateByCheckpointTagInitial(_tag);
	}

	[Test]
	public void stream_position_is_updated() {
		Assert.AreEqual(1, _positionTracker.LastTag.Streams["stream1"]);
		Assert.AreEqual(2, _positionTracker.LastTag.Streams["stream2"]);
	}
}
