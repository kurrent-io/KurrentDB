namespace EventStore.Projections.Core.Services.Processing {
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
}
