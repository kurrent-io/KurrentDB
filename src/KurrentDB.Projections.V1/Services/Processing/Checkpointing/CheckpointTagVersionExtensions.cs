// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public static class CheckpointTagVersionExtensions {
	public static CheckpointTag AdjustBy(this CheckpointTagVersion self, PositionTagger tagger, ProjectionVersion version) {
		if (self.SystemVersion == ProjectionConstants.SubsystemVersion && self.Version.Version == version.Version
														  && self.Version.ProjectionId == version.ProjectionId)
			return self.Tag;

		return tagger.AdjustTag(self.Tag);
	}
}
