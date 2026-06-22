// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Components.Scavenges;

public class ScavengeHistoryEntry {
	public string ScavengeId { get; init; } = "";
	public string NodeEndpoint { get; init; } = "";
	public DateTime? StartTime { get; set; }
	public DateTime? EndTime { get; set; }
	public string Result { get; set; }

	public bool IsRunning => EndTime == null;
}
