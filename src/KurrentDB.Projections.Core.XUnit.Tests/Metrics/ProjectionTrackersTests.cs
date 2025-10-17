// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;
using KurrentDB.Projections.Core.Metrics;
using Xunit;

namespace KurrentDB.Projections.Core.XUnit.Tests.Metrics;

public class ProjectionTrackersTest {
	private static MeterListener SetUpTrackers(
		Func<Meter, string, IProjectionStateSerializationTracker> serializationTrackerfactory,
		out ProjectionTrackers trackers,
		out IReadOnlyList<double> measurements,
		out IReadOnlyList<string?[]> tags) {

		var meter = new Meter("Projections");
		trackers = new ProjectionTrackers(
			_ => IProjectionExecutionTracker.NoOp,
			name => serializationTrackerfactory(meter, name));

		var seenTags = new List<string?[]>();
		var seenMeasurements = new List<double>();

		var meterListener = SetUpMeterListener(meter, (_, measurement, tags, _) => {
			seenMeasurements.Add(measurement);
			foreach (var tag in tags)
				seenTags.Add([tag.Key, tag.Value as string]);
		});

		measurements = seenMeasurements;
		tags = seenTags;
		return meterListener;
	}

	private static MeterListener SetUpMeterListener(Meter meter, MeasurementCallback<double> callback) {
		var meterListener = new MeterListener {
			InstrumentPublished = (instrument, listener) => {
				if (instrument.Meter == meter) {
					listener.EnableMeasurementEvents(instrument);
				}
			}
		};
		meterListener.SetMeasurementEventCallback(callback);
		meterListener.Start();
		return meterListener;
	}

	private static void RunTrackers(ProjectionTrackers trackers) {
		var start = Instant.Now;
		var projection1 = trackers.GetSerializationTrackerForProjection("projection1");
		var projection2 = trackers.GetSerializationTrackerForProjection("projection2");

		projection1.StateSerialized(start);
		projection1.StateSerialized(start);
		projection2.StateSerialized(start);
		projection1.StateSerialized(start);
		projection2.StateSerialized(start);
	}

	[Fact]
	public void serialization_tracker_works() {
		using var listener = SetUpTrackers((meter, name) => {
			var serializationDurationMetric = new DurationMaxMetric(meter, "serialization-duration-max", legacyNames: false);
			var serializationDurationTracker = new ProjectionStateSerializationTracker(
				tracker: new DurationMaxTracker(
					metric: serializationDurationMetric,
					name: name,
					expectedScrapeIntervalSeconds: 30
				));
			return serializationDurationTracker;
		}, out var trackers, out var measurements, out var tags);

		RunTrackers(trackers);
		listener.RecordObservableInstruments();

		Assert.Equal([
			["name", "projection1"], ["range", "32-40 seconds"],
			["name", "projection2"], ["range", "32-40 seconds"]
		], tags);

		Assert.Equal(2, measurements.Count);
		Assert.All(measurements, x => Assert.True(x > 0.0));
	}
}
