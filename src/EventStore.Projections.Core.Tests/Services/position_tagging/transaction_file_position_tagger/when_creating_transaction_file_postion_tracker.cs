// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.position_tagging.transaction_file_position_tagger;

[TestFixture]
public class when_creating_transaction_file_postion_tracker {
	private PositionTagger _tagger;
	private PositionTracker _positionTracker;

	[SetUp]
	public void when() {
		_tagger = new TransactionFilePositionTagger(0);
		_positionTracker = new PositionTracker(_tagger);
	}

	[Test]
	public void it_can_be_updated() {
		// even not initialized (UpdateToZero can be removed)
		var newTag = CheckpointTag.FromPosition(0, 100, 50);
		_positionTracker.UpdateByCheckpointTagInitial(newTag);
	}

	[Test]
	public void initial_position_cannot_be_set_twice() {
		Assert.Throws<InvalidOperationException>(() => {
			var newTag = CheckpointTag.FromPosition(0, 100, 50);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
		});
	}

	[Test]
	public void it_can_be_updated_to_zero() {
		_positionTracker.UpdateByCheckpointTagInitial(_tagger.MakeZeroCheckpointTag());
	}

	[Test]
	public void it_cannot_be_updated_forward() {
		Assert.Throws<InvalidOperationException>(() => {
			var newTag = CheckpointTag.FromPosition(0, 100, 50);
			_positionTracker.UpdateByCheckpointTagForward(newTag);
		});
	}
}
