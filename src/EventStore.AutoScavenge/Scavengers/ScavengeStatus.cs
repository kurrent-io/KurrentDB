// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge.Scavengers;

public enum ScavengeStatus {
	Unknown, // couldn't get the status
	InProgress,
	Success, // completed successfuly
	Stopped, // intentionally stopped
	Errored, // completed with an error
	NotRunningUnknown, // not running but don't know if it completed/stopped/errored/interrupted
}
