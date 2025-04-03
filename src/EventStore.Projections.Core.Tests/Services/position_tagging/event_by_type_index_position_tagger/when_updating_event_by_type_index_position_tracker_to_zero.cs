// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.EventByType;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.event_by_type_index_position_tagger;

[TestFixture]
public class when_updating_event_by_type_index_position_tracker_to_zero {
	private EventByTypeIndexPositionTagger _tagger;
	private PositionTracker _positionTracker;

	[SetUp]
	public void When() {
		_tagger = new EventByTypeIndexPositionTagger(0, new[] {"type1", "type2"});
		_positionTracker = new PositionTracker(_tagger);
		// when 

		_positionTracker.UpdateByCheckpointTagInitial(_tagger.MakeZeroCheckpointTag());
	}

	[Test]
	public void streams_are_set_up() {
		Assert.Contains("type1", _positionTracker.LastTag.Streams.Keys);
		Assert.Contains("type2", _positionTracker.LastTag.Streams.Keys);
	}

	[Test]
	public void stream_position_is_minus_one() {
		Assert.AreEqual(ExpectedVersion.NoStream, _positionTracker.LastTag.Streams["type1"]);
		Assert.AreEqual(ExpectedVersion.NoStream, _positionTracker.LastTag.Streams["type2"]);
	}

	[Test]
	public void tf_position_is_zero() {
		Assert.AreEqual(new TFPos(0, -1), _positionTracker.LastTag.Position);
	}
}
