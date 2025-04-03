// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using EventStore.Core.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class DurationTrackerTests : IDisposable {
	private readonly TestMeterListener<double> _listener;
	private readonly FakeClock _clock = new();
	private readonly DurationTracker _sut;

	public DurationTrackerTests() {
		var meter = new Meter($"{typeof(DurationTrackerTests)}");
		_listener = new TestMeterListener<double>(meter);
		var durationMetric = new DurationMetric(meter, "the-histogram", legacyNames: false, _clock);
		_sut = new DurationTracker(durationMetric, "the-duration");
	}

	public void Dispose() {
		_listener.Dispose();
	}

	[Fact]
	public void records_success() {
		_clock.SecondsSinceEpoch = 500;
		using (_sut.Start()) {
			_clock.SecondsSinceEpoch = 501;
		}

		AssertMeasurements("successful", 1);
	}

	[Fact]
	public void records_failure() {
		_clock.SecondsSinceEpoch = 500;
		using (var duration = _sut.Start()) {
			_clock.SecondsSinceEpoch = 501;
			duration.SetException(new Exception("failed"));
		}

		AssertMeasurements("failed", 1);
	}

	void AssertMeasurements(
		string expectedStatus,
		int expectedValue) {

		Assert.Collection(
			_listener.RetrieveMeasurements("the-histogram-seconds"),
			m => {
				Assert.Equal(expectedValue, m.Value);
				Assert.Collection(
					m.Tags,
					t => {
						Assert.Equal("activity", t.Key);
						Assert.Equal("the-duration", t.Value);
					},
					t => {
						Assert.Equal("status", t.Key);
						Assert.Equal(expectedStatus, t.Value);
					});
			});
	}
}
