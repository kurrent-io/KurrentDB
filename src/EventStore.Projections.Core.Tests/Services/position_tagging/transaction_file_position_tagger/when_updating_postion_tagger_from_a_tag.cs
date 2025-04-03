// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.transaction_file_position_tagger;

[TestFixture]
public class when_updating_postion_tagger_from_a_tag {
	private PositionTagger _tagger;
	private CheckpointTag _tag;
	private PositionTracker _positionTracker;

	[SetUp]
	public void When() {
		// given
		var tagger = new TransactionFilePositionTagger(0);
		var positionTracker = new PositionTracker(tagger);

		var newTag = CheckpointTag.FromPosition(0, 100, 50);
		positionTracker.UpdateByCheckpointTagInitial(newTag);
		_tag = positionTracker.LastTag;
		_tagger = new TransactionFilePositionTagger(0);
		_positionTracker = new PositionTracker(_tagger);
		// when

		_positionTracker.UpdateByCheckpointTagInitial(_tag);
	}

	[Test]
	public void position_is_updated() {
		Assert.AreEqual(50, _positionTracker.LastTag.PreparePosition);
		Assert.AreEqual(100, _positionTracker.LastTag.CommitPosition);
	}
}
