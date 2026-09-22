// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core;
using KurrentDB.Projections.Core.Services;

namespace KurrentDB.Components.Projections;

// Statistics the v2 engine leaves at their defaults. CoreProjectionV2.PublishStatistics builds a fresh
// ProjectionStatistics each tick and fills in only status, events processed and the partition-state cache
// counters, so the rest arrive as 0/null — indistinguishable from a genuine zero once rendered. The views
// show an em dash for these instead, which is honest about "we don't know" rather than asserting a number.
// As each field gets plumbed through the v2 engine, drop it from Reports and the views need no edits.
public enum ProjectionStat {
	Progress,
	BufferedEvents,
	CheckpointStatus,
	ReadsWrites,
	WriteQueues,
}

public static class ProjectionStatSupport {
	public const string NotReportedTooltip = "Not reported by the v2 projection engine";

	// v2 reports none of these today. Each arm flips to true as its field is plumbed through
	// CoreProjectionV2.PublishStatistics, and both views pick the value up with no markup change.
	public static bool Reports(this ProjectionStatistics stats, ProjectionStat stat) =>
		stats.EngineVersion != ProjectionConstants.EngineV2 || stat switch {
			// Needs the read loop's last-processed position and a TF-end denominator.
			ProjectionStat.Progress => false,
			// Hardcoded to 0; the real depth lives in the PartitionDispatcher channels.
			ProjectionStat.BufferedEvents => false,
			// CheckpointCoordinator holds a lock for the duration of a checkpoint but doesn't surface it.
			ProjectionStat.CheckpointStatus => false,
			// The v2 engine keeps no in-flight read/write counters.
			ProjectionStat.ReadsWrites => false,
			// v2 has no before/after-checkpoint write queues: a checkpoint is one atomic multi-stream
			// write, so this pair has no v2 equivalent to plumb rather than merely being unimplemented.
			ProjectionStat.WriteQueues => false,
			_ => true
		};

	// Both engines cache partition state, they just report it through different fields: v1 counts the
	// per-projection cache in PartitionsCached, v2 the shared SIEVE cache in PartitionStateCacheSize.
	// This is a real number under both engines, so it is re-pointed rather than dashed out.
	public static long CachedPartitions(this ProjectionStatistics stats) =>
		stats.EngineVersion == ProjectionConstants.EngineV2
			? stats.PartitionStateCacheSize
			: stats.PartitionsCached;
}
