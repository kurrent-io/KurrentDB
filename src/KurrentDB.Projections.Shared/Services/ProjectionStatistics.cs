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

	public int StateSizeThreshold { get; set; }

	public int StateSizeLimit { get; set; }

	/// <summary>
	/// Approximate current item count in the V2 projection's shared partition-state cache.
	/// The underlying counter is maintained via an asynchronous SIEVE eviction callback, so the
	/// reported value can temporarily exceed MaxPartitionStateCacheSize between a sweep and the
	/// callback firing. Treat it as an upper-bounded observation, not a hard invariant.
	/// </summary>
	public long PartitionStateCacheSize { get; set; }

	/// <summary>
	/// Monotonic count of partition-state entries evicted from the V2 shared cache since the
	/// engine started. Useful as a pressure signal (rising eviction rate = under-sized cache).
	/// </summary>
	public long PartitionStateCacheEvictions { get; set; }

	public ProjectionStatistics Clone() {
		return (ProjectionStatistics)MemberwiseClone();
	}
}
