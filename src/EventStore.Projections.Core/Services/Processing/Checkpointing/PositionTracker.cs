// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Projections.Core.Services.Processing.Checkpointing;

public class PositionTracker {
	private readonly PositionTagger _positionTagger;
	private CheckpointTag _lastTag = null;

	public PositionTracker(PositionTagger positionTagger) {
		_positionTagger = positionTagger;
	}

	public CheckpointTag LastTag {
		get { return _lastTag; }
	}

	public void UpdateByCheckpointTagForward(CheckpointTag newTag) {
		if (_lastTag == null)
			throw new InvalidOperationException("Initial position was not set");
		if (newTag <= _lastTag)
			throw new InvalidOperationException(
				string.Format("Event at checkpoint tag {0} has been already processed", newTag));
		InternalUpdate(newTag);
	}

	public void UpdateByCheckpointTagInitial(CheckpointTag checkpointTag) {
		if (_lastTag != null)
			throw new InvalidOperationException("Position tagger has be already updated");
		InternalUpdate(checkpointTag);
	}

	private void InternalUpdate(CheckpointTag newTag) {
		if (!_positionTagger.IsCompatible(newTag))
			throw new InvalidOperationException("Cannot update by incompatible checkpoint tag");
		_lastTag = newTag;
	}

	public void Initialize() {
		_lastTag = null;
	}
}
