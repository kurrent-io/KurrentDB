// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Projections.Core.Services.Http;

public class ProjectionStatisticsHttpFormatted {
	public ProjectionStatisticsHttpFormatted(ProjectionStatistics source, Func<string, string> makeAbsoluteUrl) {
		Status = source.Status;
		StateReason = source.StateReason;
		Name = source.Name;
		EffectiveName = source.Name;
		Epoch = source.Epoch;
		Version = source.Version;
		Mode = source.Mode;
		Position = (source.Position ?? (object)"").ToString();
		Progress = source.Progress;
		LastCheckpoint = source.LastCheckpoint;
		EventsProcessedAfterRestart = source.EventsProcessedAfterRestart;
		BufferedEvents = source.BufferedEvents;
		CheckpointStatus = source.CheckpointStatus;
		WritePendingEventsBeforeCheckpoint = source.WritePendingEventsBeforeCheckpoint;
		WritePendingEventsAfterCheckpoint = source.WritePendingEventsAfterCheckpoint;
		ReadsInProgress = source.ReadsInProgress;
		WritesInProgress = source.WritesInProgress;
		CoreProcessingTime = source.CoreProcessingTime;
		PartitionsCached = source.PartitionsCached;
		var statusLocalUrl = $"/projection/{source.Name}";
		StatusUrl = makeAbsoluteUrl(statusLocalUrl);
		StateUrl = makeAbsoluteUrl($"{statusLocalUrl}/state");
		ResultUrl = makeAbsoluteUrl($"{statusLocalUrl}/result");
		QueryUrl = makeAbsoluteUrl($"{statusLocalUrl}/query?config=yes");
		if (!string.IsNullOrEmpty(source.ResultStreamName))
			ResultStreamUrl = makeAbsoluteUrl($"/streams/{Uri.EscapeDataString(source.ResultStreamName)}");
		DisableCommandUrl = makeAbsoluteUrl($"{statusLocalUrl}/command/disable");
		EnableCommandUrl = makeAbsoluteUrl($"{statusLocalUrl}/command/enable");
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
