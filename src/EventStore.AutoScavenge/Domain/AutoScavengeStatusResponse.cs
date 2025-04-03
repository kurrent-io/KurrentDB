// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NCrontab;

namespace EventStore.AutoScavenge.Domain;

public record AutoScavengeStatusResponse(
	AutoScavengeStatusResponse.Status State,
	CrontabSchedule? Schedule,
	TimeSpan? TimeUntilNextCycle) {

	public enum Status {
		NotConfigured,
		Waiting,
		InProgress,
		Pausing,
		Paused,
	}
}
