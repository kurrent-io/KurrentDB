// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Projections.Core.Services.Http;

public class ProjectionStatisticsHttpFormatted {
	public ProjectionStatisticsHttpFormatted(ProjectionStatistics source, Func<string, string> makeAbsoluteUrl) {
		this.Status = source.Status;
		this.StateReason = source.StateReason;
		this.Name = source.Name;
		this.EffectiveName = source.Name;
		this.Epoch = source.Epoch;
		this.Version = source.Version;
		this.Mode = source.Mode;
		this.Position = (source.Position ?? (object)"").ToString();
		this.Progress = source.Progress;
		this.LastCheckpoint = source.LastCheckpoint;
		this.EventsProcessedAfterRestart = source.EventsProcessedAfterRestart;
		this.BufferedEvents = source.BufferedEvents;
		this.CheckpointStatus = source.CheckpointStatus;
		this.WritePendingEventsBeforeCheckpoint = source.WritePendingEventsBeforeCheckpoint;
		this.WritePendingEventsAfterCheckpoint = source.WritePendingEventsAfterCheckpoint;
		this.ReadsInProgress = source.ReadsInProgress;
		this.WritesInProgress = source.WritesInProgress;
		this.CoreProcessingTime = source.CoreProcessingTime;
		this.PartitionsCached = source.PartitionsCached;
		var statusLocalUrl = "/projection/" + source.Name;
		this.StatusUrl = makeAbsoluteUrl(statusLocalUrl);
		this.StateUrl = makeAbsoluteUrl(statusLocalUrl + "/state");
		this.ResultUrl = makeAbsoluteUrl(statusLocalUrl + "/result");
		this.QueryUrl = makeAbsoluteUrl(statusLocalUrl + "/query?config=yes");
		if (!string.IsNullOrEmpty(source.ResultStreamName))
			this.ResultStreamUrl = makeAbsoluteUrl("/streams/" + Uri.EscapeDataString(source.ResultStreamName));
		this.DisableCommandUrl = makeAbsoluteUrl(statusLocalUrl + "/command/disable");
		this.EnableCommandUrl = makeAbsoluteUrl(statusLocalUrl + "/command/enable");
	}

	public long CoreProcessingTime { get; set; }

	public long Version { get; set; }

	public long Epoch { get; set; }

	public string EffectiveName { get; set; }

	public int WritesInProgress { get; set; }

	public int ReadsInProgress { get; set; }

	public int PartitionsCached { get; set; }

	public string Status { get; set; }

	public string StateReason { get; set; }

	public string Name { get; set; }

	public ProjectionMode Mode { get; set; }

	public string Position { get; set; }

	public float Progress { get; set; }

	public string LastCheckpoint { get; set; }

	public int EventsProcessedAfterRestart { get; set; }

	public string StatusUrl { get; set; }

	public string StateUrl { get; set; }

	public string ResultUrl { get; set; }

	public string QueryUrl { get; set; }

	public string ResultStreamUrl { get; set; }

	public string EnableCommandUrl { get; set; }

	public string DisableCommandUrl { get; set; }

	public string CheckpointStatus { get; set; }

	public int BufferedEvents { get; set; }

	public int WritePendingEventsBeforeCheckpoint { get; set; }

	public int WritePendingEventsAfterCheckpoint { get; set; }
}
