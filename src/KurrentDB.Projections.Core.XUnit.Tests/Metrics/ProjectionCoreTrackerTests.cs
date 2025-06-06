// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using KurrentDB.Core.Metrics;
using KurrentDB.Projections.Core.Metrics;
using Xunit;

namespace KurrentDB.Projections.Core.XUnit.Tests.Metrics;

public class ProjectionCoreTrackerTests {
	private readonly ProjectionCoreTracker _sut;
	private readonly Meter _meter;

	public ProjectionCoreTrackerTests() {
		_meter = new Meter("projection");
		var executionDurationMetric = new DurationMetric(_meter, "execution-duration", legacyNames: false);
		_sut = new ProjectionCoreTracker(executionDurationMetric);
	}

	private static void SetUpMeterListener(Meter meter, MeasurementCallback<double> callback) {
		var meterListener = new MeterListener {
			InstrumentPublished = (instrument, listener) => {
				if (instrument.Meter == meter) {
					listener.EnableMeasurementEvents(instrument);
				}
			}
		};
		meterListener.SetMeasurementEventCallback(callback);
		meterListener.Start();
	}

	[Fact]
	public void can_measure_js_call_duration() {
		var seenTags = new List<string?[]>();
		SetUpMeterListener(_meter, (_, measurement, tags, _) => {
			Assert.True(measurement > 0.0);
			foreach(var tag in tags)
				seenTags.Add([tag.Key, tag.Value as string]);
		});

		var engine = new Engine();
		engine.Execute("function test(a, b, c, d) { return 10; }");

		var testFunction = engine.GetValue("test") as ScriptFunction;
		var testArg = JsValue.FromObject(engine, 5);

		Assert.Equal(10, _sut.MeasureJsCallDuration.Call("projection0", "test0", testFunction).AsNumber());
		Assert.Equal(10, _sut.MeasureJsCallDuration.Call("projection1", "test1", testFunction, testArg).AsNumber());
		Assert.Equal(10, _sut.MeasureJsCallDuration.Call("projection2", "test2", testFunction, testArg, testArg).AsNumber());
		Assert.Equal(10, _sut.MeasureJsCallDuration.Call("projection3", "test3", testFunction, testArg, testArg, testArg).AsNumber());
		Assert.Equal(10, _sut.MeasureJsCallDuration.Call("projection0", "test0", testFunction).AsNumber());

		Assert.Equal([
			["projection", "projection0"], ["jsFunction", "test0"],
			["projection", "projection1"], ["jsFunction", "test1"],
			["projection", "projection2"], ["jsFunction", "test2"],
			["projection", "projection3"], ["jsFunction", "test3"],
			["projection", "projection0"], ["jsFunction", "test0"],
		], seenTags);
	}
}
