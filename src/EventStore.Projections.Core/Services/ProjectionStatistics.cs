// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Management;

namespace EventStore.Projections.Core.Services;

public class ProjectionStatistics {
	//TODO: resolve name collisions...

	public string Status { get; set; }

	public bool Enabled { get; set; }

	public ManagedProjectionState LeaderStatus { get; set; }

	public string StateReason { get; set; }

	public string Name { get; set; }

	public long ProjectionId { get; set; }

	public long Epoch { get; set; }

	public long Version { get; set; }

	public ProjectionMode Mode { get; set; }

	public string Position { get; set; }

	public float Progress { get; set; }

	public string LastCheckpoint { get; set; }

	public int EventsProcessedAfterRestart { get; set; }

	public int BufferedEvents { get; set; }

	public string CheckpointStatus { get; set; }

	public int WritePendingEventsBeforeCheckpoint { get; set; }

	public int WritePendingEventsAfterCheckpoint { get; set; }

	public int PartitionsCached { get; set; }

	public int ReadsInProgress { get; set; }

	public int WritesInProgress { get; set; }

	public string EffectiveName { get; set; }

	public string ResultStreamName { get; set; }

	public long CoreProcessingTime { get; set; }

	public ProjectionStatistics Clone() {
		return (ProjectionStatistics)MemberwiseClone();
	}

	protected bool Equals(ProjectionStatistics other) {
		return string.Equals(Status, other.Status) && Enabled.Equals(other.Enabled)
		                                           && LeaderStatus == other.LeaderStatus &&
		                                           string.Equals(StateReason, other.StateReason)
		                                           && string.Equals(Name, other.Name) &&
		                                           ProjectionId == other.ProjectionId && Epoch == other.Epoch
		                                           && Version == other.Version && Mode == other.Mode &&
		                                           Equals(Position, other.Position)
		                                           && Progress.Equals(other.Progress) &&
		                                           string.Equals(LastCheckpoint, other.LastCheckpoint)
		                                           && EventsProcessedAfterRestart ==
		                                           other.EventsProcessedAfterRestart
		                                           && BufferedEvents == other.BufferedEvents &&
		                                           string.Equals(CheckpointStatus, other.CheckpointStatus)
		                                           && WritePendingEventsBeforeCheckpoint ==
		                                           other.WritePendingEventsBeforeCheckpoint
		                                           && WritePendingEventsAfterCheckpoint ==
		                                           other.WritePendingEventsAfterCheckpoint
		                                           && PartitionsCached == other.PartitionsCached &&
		                                           ReadsInProgress == other.ReadsInProgress
		                                           && WritesInProgress == other.WritesInProgress &&
		                                           string.Equals(EffectiveName, other.EffectiveName)
		                                           && string.Equals(ResultStreamName, other.ResultStreamName) &&
		                                           CoreProcessingTime == other.CoreProcessingTime;
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj)) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != this.GetType()) return false;
		return Equals((ProjectionStatistics)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int hashCode = (Status != null ? Status.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ Enabled.GetHashCode();
			hashCode = (hashCode * 397) ^ (int)LeaderStatus;
			hashCode = (hashCode * 397) ^ (StateReason != null ? StateReason.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ ProjectionId.GetHashCode();
			hashCode = (hashCode * 397) ^ Epoch.GetHashCode();
			hashCode = (hashCode * 397) ^ Version.GetHashCode();
			hashCode = (hashCode * 397) ^ (int)Mode;
			hashCode = (hashCode * 397) ^ (Position != null ? Position.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ Progress.GetHashCode();
			hashCode = (hashCode * 397) ^ (LastCheckpoint != null ? LastCheckpoint.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ EventsProcessedAfterRestart;
			hashCode = (hashCode * 397) ^ BufferedEvents;
			hashCode = (hashCode * 397) ^ (CheckpointStatus != null ? CheckpointStatus.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ WritePendingEventsBeforeCheckpoint;
			hashCode = (hashCode * 397) ^ WritePendingEventsAfterCheckpoint;
			hashCode = (hashCode * 397) ^ PartitionsCached;
			hashCode = (hashCode * 397) ^ ReadsInProgress;
			hashCode = (hashCode * 397) ^ WritesInProgress;
			hashCode = (hashCode * 397) ^ (EffectiveName != null ? EffectiveName.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ (ResultStreamName != null ? ResultStreamName.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ CoreProcessingTime.GetHashCode();
			return hashCode;
		}
	}
}
