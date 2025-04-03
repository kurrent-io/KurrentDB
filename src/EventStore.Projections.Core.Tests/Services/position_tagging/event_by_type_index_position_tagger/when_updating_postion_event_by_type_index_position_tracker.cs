// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.EventByType;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.event_by_type_index_position_tagger;

[TestFixture]
public class when_updating_postion_event_by_type_index_position_tracker {
	private EventByTypeIndexPositionTagger _tagger;
	private PositionTracker _positionTracker;

	[SetUp]
	public void When() {
		// given
		_tagger = new EventByTypeIndexPositionTagger(0, new[] {"type1", "type2"});
		_positionTracker = new PositionTracker(_tagger);
		var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(10, 5),
			new Dictionary<string, long> {{"type1", 1}, {"type2", 2}});
		var newTag2 = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(20, 15),
			new Dictionary<string, long> {{"type1", 1}, {"type2", 3}});
		_positionTracker.UpdateByCheckpointTagInitial(newTag);
		_positionTracker.UpdateByCheckpointTagForward(newTag2);
	}

	[Test]
	public void stream_position_is_updated() {
		Assert.AreEqual(1, _positionTracker.LastTag.Streams["type1"]);
		Assert.AreEqual(3, _positionTracker.LastTag.Streams["type2"]);
	}

	[Test]
	public void tf_position_is_updated() {
		Assert.AreEqual(new TFPos(20, 15), _positionTracker.LastTag.Position);
	}

	[Test]
	public void cannot_update_to_the_same_position() {
		var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(20, 15),
			new Dictionary<string, long> {{"type1", 1}, {"type2", 3}});
		Assert.Throws<InvalidOperationException>(() => { _positionTracker.UpdateByCheckpointTagForward(newTag); });
	}

	[Test]
	public void can_update_to_the_same_index_position_but_tf() {
		var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(30, 25),
			new Dictionary<string, long> {{"type1", 1}, {"type2", 3}});
		_positionTracker.UpdateByCheckpointTagForward(newTag);
	}

	[Test]
	public void it_cannot_be_updated_with_other_stream() {
		// even not initialized (UpdateToZero can be removed)
		var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(30, 25),
			new Dictionary<string, long> {{"type1", 1}, {"type3", 3}});
		Assert.Throws<InvalidOperationException>(() => { _positionTracker.UpdateByCheckpointTagForward(newTag); });
	}

	//TODO: write tests on updating with incompatible snapshot loaded
}
