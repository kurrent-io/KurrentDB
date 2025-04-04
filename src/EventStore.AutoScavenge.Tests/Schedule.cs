// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NCrontab;

namespace EventStore.AutoScavenge.Tests;

public static class Schedule {
	public static CrontabSchedule EveryHour => CrontabSchedule.Parse("0 * * * *");
	public static CrontabSchedule EveryDay => CrontabSchedule.Parse("0 0 * * *");
	public static CrontabSchedule EveryWeek => CrontabSchedule.Parse("0 0 * * 0");
}
