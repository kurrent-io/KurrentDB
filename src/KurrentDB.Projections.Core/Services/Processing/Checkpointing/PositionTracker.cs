// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public class PositionTracker(PositionTagger positionTagger) {
	public CheckpointTag LastTag { get; private set; }

	public void UpdateByCheckpointTagForward(CheckpointTag newTag) {
		if (LastTag == null)
			throw new InvalidOperationException("Initial position was not set");
		if (newTag <= LastTag)
			throw new InvalidOperationException($"Event at checkpoint tag {newTag} has been already processed");
		InternalUpdate(newTag);
	}

	public void UpdateByCheckpointTagInitial(CheckpointTag checkpointTag) {
		if (LastTag != null)
			throw new InvalidOperationException("Position tagger has be already updated");
		InternalUpdate(checkpointTag);
	}

	private void InternalUpdate(CheckpointTag newTag) {
		if (!positionTagger.IsCompatible(newTag))
			throw new InvalidOperationException("Cannot update by incompatible checkpoint tag");
		LastTag = newTag;
	}

	public void Initialize() {
		LastTag = null;
	}
}
