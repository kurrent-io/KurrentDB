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
public class when_creating_event_by_type_index_position_tracker {
	private EventByTypeIndexPositionTagger _tagger;
	private PositionTracker _positionTracker;

	[SetUp]
	public void when() {
		_tagger = new EventByTypeIndexPositionTagger(0, new[] {"type1", "type2"});
		_positionTracker = new PositionTracker(_tagger);
	}

	[Test]
	public void it_can_be_updated_with_correct_event_types() {
		// even not initialized (UpdateToZero can be removed)
		var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(100, 50),
			new Dictionary<string, long> {{"type1", 10}, {"type2", 20}});
		_positionTracker.UpdateByCheckpointTagInitial(newTag);
	}

	[Test]
	public void it_cannot_be_updated_with_other_event_types() {
		var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(100, 50),
			new Dictionary<string, long> {{"type1", 10}, {"type3", 20}});
		Assert.Throws<InvalidOperationException>(() => { _positionTracker.UpdateByCheckpointTagInitial(newTag); });
	}

	[Test]
	public void it_cannot_be_updated_forward() {
		var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(100, 50),
			new Dictionary<string, long> {{"type1", 10}, {"type2", 20}});
		Assert.Throws<InvalidOperationException>(() => { _positionTracker.UpdateByCheckpointTagForward(newTag); });
	}

	[Test]
	public void initial_position_cannot_be_set_twice() {
		Assert.Throws<InvalidOperationException>(() => {
			var newTag = CheckpointTag.FromEventTypeIndexPositions(0, new TFPos(100, 50),
				new Dictionary<string, long> {{"type1", 10}, {"type2", 20}});
			_positionTracker.UpdateByCheckpointTagForward(newTag);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
		});
	}

	[Test]
	public void it_can_be_updated_to_zero() {
		_positionTracker.UpdateByCheckpointTagInitial(_tagger.MakeZeroCheckpointTag());
	}
}
