// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.SecondaryIndexing.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KurrentDB.SecondaryIndexing.Tests.Diagnostics;

public class SecondaryIndexProgressTrackerTests {
	[Theory]
	[InlineData(2.5)]
	[InlineData(0.25)]
	public void commit_duration_records_total_elapsed_time_in_seconds(double commitDurationSeconds) {
		using var meter = new Meter("test");
		var clock = new FakeTimeProvider();
		var tracker = new SecondaryIndexProgressTracker(
			"default",
			"kurrentdb",
			meter,
			clock,
			NullLogger<SecondaryIndexProgressTracker>.Instance);

		List<(double Value, KeyValuePair<string, object?>[] Tags)> measurements = [];
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, l) => {
			if (instrument.Meter == meter && instrument.Name == "kurrentdb.indexes.secondary.commit.seconds")
				l.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>(
			(_, value, tags, _) => measurements.Add((value, tags.ToArray())));
		listener.Start();

		using (tracker.StartCommitDuration())
			clock.Advance(TimeSpan.FromSeconds(commitDurationSeconds));

		var measurement = Assert.Single(measurements);
		Assert.Equal(commitDurationSeconds, measurement.Value, precision: 6);
		Assert.Contains(measurement.Tags, t => t is { Key: "index", Value: "default" });
	}
}
