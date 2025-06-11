//// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
//// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;
using KurrentDB.Projections.Core.Metrics;
using Xunit;

namespace KurrentDB.Projections.Core.XUnit.Tests.Metrics;

public class ProjectionExecutionTrackersTests {
	private static MeterListener SetUpTrackers(
		Func<Meter, string, IProjectionExecutionTracker> factory,
		out ProjectionExecutionTrackers trackers,
		out IReadOnlyList<double> measurements,
		out IReadOnlyList<string?[]> tags) {

		var meter = new Meter("Projections");
		trackers = new ProjectionExecutionTrackers(name => factory(meter, name));

		var seenTags = new List<string?[]>();
		var seenMeasurements = new List<double>();

		var meterListener = SetUpMeterListener(meter, (_, measurement, tags, _) => {
			seenMeasurements.Add(measurement);
			foreach(var tag in tags)
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

	private static void ExecuteCalls(ProjectionExecutionTrackers trackers) {
		var start = Instant.Now;
		var projection1 = trackers.GetTrackerForProjection("projection1");
		var projection2 = trackers.GetTrackerForProjection("projection2");

		projection1.CallExecuted(start, "func0");
		projection1.CallExecuted(start, "func1");
		projection2.CallExecuted(start, "func0");
		projection1.CallExecuted(start, "func0");
		projection2.CallExecuted(start, "func2");
	}

	[Fact]
	public void histogram_tracker_works() {
		using var _ = SetUpTrackers((meter, name) => {
			var executionDurationMetric = new DurationMetric(meter, "execution-duration", legacyNames: false);
			var executionDurationTracker = new ProjectionExecutionHistogramTracker(name, executionDurationMetric);
			return executionDurationTracker;
		}, out var trackers, out var measurements, out var tags);

		ExecuteCalls(trackers);

		Assert.Equal([
			["projection", "projection1"], ["jsFunction", "func0"],
			["projection", "projection1"], ["jsFunction", "func1"],
			["projection", "projection2"], ["jsFunction", "func0"],
			["projection", "projection1"], ["jsFunction", "func0"],
			["projection", "projection2"], ["jsFunction", "func2"],
		], tags);

		Assert.Equal(5, measurements.Count);
		Assert.All(measurements, x => Assert.True(x > 0.0));
	}

	[Fact]
	public void max_tracker_works() {
		using var listener = SetUpTrackers((meter, name) => {
			var executionDurationMaxMetric = new DurationMaxMetric(meter, "execution-duration-max", legacyNames: false);
			var executionDurationMaxTracker = new ProjectionExecutionMaxTracker(
				tracker: new DurationMaxTracker(executionDurationMaxMetric, name, 30));
			return executionDurationMaxTracker;
		}, out var trackers, out var measurements, out var tags);

		ExecuteCalls(trackers);
		listener.RecordObservableInstruments();

		Assert.Equal([
			["name", "projection1"], ["range", "32-40 seconds"],
			["name", "projection2"], ["range", "32-40 seconds"]
		], tags);

		Assert.Equal(2, measurements.Count);
		Assert.All(measurements, x => Assert.True(x > 0.0));
	}

	[Fact]
	public void composite_tracker_works() {
		using var _ = SetUpTrackers((meter, name) => {
			var executionDurationMetric = new DurationMetric(meter, "execution-duration", legacyNames: false);
			var executionDurationTracker = new ProjectionExecutionHistogramTracker(name, executionDurationMetric);
			return new CompositeProjectionExecutionTracker([executionDurationTracker, executionDurationTracker]);
		}, out var trackers, out var measurements, out var tags);

		ExecuteCalls(trackers);

		Assert.Equal([
			["projection", "projection1"], ["jsFunction", "func0"],
			["projection", "projection1"], ["jsFunction", "func0"],

			["projection", "projection1"], ["jsFunction", "func1"],
			["projection", "projection1"], ["jsFunction", "func1"],

			["projection", "projection2"], ["jsFunction", "func0"],
			["projection", "projection2"], ["jsFunction", "func0"],

			["projection", "projection1"], ["jsFunction", "func0"],
			["projection", "projection1"], ["jsFunction", "func0"],

			["projection", "projection2"], ["jsFunction", "func2"],
			["projection", "projection2"], ["jsFunction", "func2"],
		], tags);

		Assert.Equal(10, measurements.Count);
		Assert.All(measurements, x => Assert.True(x > 0.0));
	}
}
