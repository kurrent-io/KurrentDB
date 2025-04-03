// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Services.Processing.Checkpointing;

public struct CheckpointTagVersion {
	public ProjectionVersion Version;
	public int SystemVersion;
	public CheckpointTag Tag;
	public Dictionary<string, JToken> ExtraMetadata;

	public CheckpointTag AdjustBy(PositionTagger tagger, ProjectionVersion version) {
		if (SystemVersion == ProjectionsSubsystem.VERSION && Version.Version == version.Version
		                                                  && Version.ProjectionId == version.ProjectionId)
			return Tag;

		return tagger.AdjustTag(Tag);
	}
}
