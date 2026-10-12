// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using KurrentDB.AutoScavenge.Converters;
using KurrentDB.POC.IO.Core.Serialization;
using NCrontab;

namespace KurrentDB.AutoScavenge.Domain;

public record AutoScavengeStatusResponse(
	[property: JsonConverter(typeof(EnumConverterWithDefault<AutoScavengeStatusResponse.Status>))]
	AutoScavengeStatusResponse.Status State,
	[property: JsonConverter(typeof(CrontableScheduleJsonConverter))]
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
