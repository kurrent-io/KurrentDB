// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Components.Scavenges;

// One row in the scavenge detail grid (Status / Space Saved / Time Taken / Result).
public class ScavengeDetailEvent {
	public string Status { get; set; } = "";
	public long SpaceSaved { get; set; }
	public string TimeTaken { get; set; } = "";
	public string Result { get; set; } = "";
}
