// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace EventStore.Projections.Core.Services.Processing;

public struct ProjectionVersion {
	public readonly long ProjectionId;
	public readonly long Epoch;
	public readonly long Version;

	public ProjectionVersion(long projectionId, long epoch, long version) {
		ProjectionId = projectionId;
		Epoch = epoch;
		Version = version;
	}

	public bool Equals(ProjectionVersion other) {
		return ProjectionId == other.ProjectionId && Epoch == other.Epoch && Version == other.Version;
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj))
			return false;
		return obj is ProjectionVersion && Equals((ProjectionVersion)obj);
	}

	public override int GetHashCode() {
		unchecked {
			var hashCode = ProjectionId.GetHashCode();
			hashCode = (hashCode * 397) ^ Epoch.GetHashCode();
			hashCode = (hashCode * 397) ^ Version.GetHashCode();
			return hashCode;
		}
	}
}
