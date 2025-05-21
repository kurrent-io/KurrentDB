// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;

namespace KurrentDB.SecondaryIndexing.Metrics;

public class SecondaryIndexMetrics {
	private static DurationMetric IndexDuration = null!;
	private static DurationMetric ReadDuration = null!;

	public static void Setup(Meter meter) {
		IndexDuration = new DurationMetric(meter, "eventstore-temp-index-duration-seconds", false);
		ReadDuration = new DurationMetric(meter, "eventstore-temp-read-duration-seconds", false);
	}

	public static Duration MeasureIndex(string name) => new(IndexDuration, name, Instant.Now);

	public static Duration MeasureRead(string name) => new(ReadDuration, name, Instant.Now);
}
