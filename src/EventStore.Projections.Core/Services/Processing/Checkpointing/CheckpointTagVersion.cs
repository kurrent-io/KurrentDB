// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
