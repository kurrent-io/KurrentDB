// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Projections.Core.Services.Management;

namespace KurrentDB.Projections.Core.Services;

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

	public Dictionary<string, int> StateSizes { get; set; }

	public ProjectionStatistics Clone() {
		return (ProjectionStatistics)MemberwiseClone();
	}
}
