// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class DurationMaxTrackerTests : IDisposable {
	private readonly TestMeterListener<double> _listener;
	private readonly FakeClock _clock = new();
	private readonly DurationMaxTracker _sut;

	public DurationMaxTrackerTests() {
		var meter = new Meter($"{typeof(DurationMaxTrackerTests)}");
		_listener = new TestMeterListener<double>(meter);
		var metric = new DurationMaxMetric(meter, "the-metric", legacyNames: false);
		_sut = new DurationMaxTracker(
			metric: metric,
			name: "the-tracker",
			expectedScrapeIntervalSeconds: 15,
			clock: _clock);
	}

	public void Dispose() {
		_listener.Dispose();
	}

	[Fact]
	public void throws_with_invalid_period_configuration() {
		var ex = Assert.Throws<ArgumentException>(() => {
			var sut = new DurationMaxTracker(
				metric: null,
				name: "the-tracker",
				expectedScrapeIntervalSeconds: 16,
				clock: _clock);
		});

		Assert.Equal(
			"ExpectedScrapeIntervalSeconds must be 0, 1, 5, 10 or a multiple of 15, but was 16",
			ex.Message);
	}

	[Fact]
	public void record_now_returns_now() {
		_clock.SecondsSinceEpoch = 500;
		var start = _clock.Now;
		_clock.SecondsSinceEpoch = 501;
		var end = _sut.RecordNow(start);
		var elapsedSeconds = end.ElapsedSecondsSince(start);
		Assert.Equal(1.000, elapsedSeconds);
	}

	[Fact]
	public void no_records() {
		AssertMeasurements(0);
	}

	[Fact]
	public void one_record() {
		AssertMeasurements(0);
		_clock.SecondsSinceEpoch = 500;
		var start = _clock.Now;
		_clock.SecondsSinceEpoch = 501;
		_sut.RecordNow(start);
		AssertMeasurements(1);
	}

	[Fact]
	public void two_records_ascending() {
		AssertMeasurements(0);
		_clock.SecondsSinceEpoch = 500;
		var start1 = _clock.Now;
		var start2 = _clock.Now;
		_clock.SecondsSinceEpoch = 501;
		_sut.RecordNow(start1); // record a 1s duration
		AssertMeasurements(1);
		_clock.SecondsSinceEpoch = 502;
		_sut.RecordNow(start2); // record a 2s duration
		AssertMeasurements(2);
	}

	[Fact]
	public void two_records_descending() {
		AssertMeasurements(0);
		_clock.SecondsSinceEpoch = 500;
		var start2 = _clock.Now;
		_clock.SecondsSinceEpoch = 502;
		_sut.RecordNow(start2); // record a 2s duration
		var start1 = _clock.Now;
		AssertMeasurements(2);
		_clock.SecondsSinceEpoch = 503;
		_sut.RecordNow(start1); // record a 1s duration
		AssertMeasurements(2);
	}

	[Fact]
	public void removes_stale_data() {
		_clock.SecondsSinceEpoch = 500;
		var start = _clock.Now;
		_clock.SecondsSinceEpoch = 501;
		_sut.RecordNow(start);
		_clock.SecondsSinceEpoch = 510;
		AssertMeasurements(1);
		_clock.SecondsSinceEpoch = 522;
		AssertMeasurements(0);
	}

	[Fact]
	public void removes_stale_data_incrementally() {
		_clock.SecondsSinceEpoch = 500;
		var start10 = _clock.Now;

		_clock.SecondsSinceEpoch = 505;
		var start9 = _clock.Now;

		_clock.SecondsSinceEpoch = 510;
		var start8 = _clock.Now;

		_clock.SecondsSinceEpoch = 515;
		var start7 = _clock.Now;

		_clock.SecondsSinceEpoch = 520;
		var start6 = _clock.Now;

		_clock.SecondsSinceEpoch = 525;
		var start5 = _clock.Now;

		// record a series of durations, each 4s apart (so in a different bucket)
		// there are 5 buckets
		AssertMeasurements(0);

		_clock.SecondsSinceEpoch = 510;
		_sut.RecordNow(start10); // record a 10s duration
		AssertMeasurements(10);

		_clock.SecondsSinceEpoch = 514;
		_sut.RecordNow(start9); // record a 9s duration
		AssertMeasurements(10);

		_clock.SecondsSinceEpoch = 518;
		_sut.RecordNow(start8); // record a 8s duration
		AssertMeasurements(10);

		_clock.SecondsSinceEpoch = 522;
		_sut.RecordNow(start7); // record a 7s duration
		AssertMeasurements(10);

		_clock.SecondsSinceEpoch = 526;
		_sut.RecordNow(start6); // record a 6s duration
		AssertMeasurements(10);

		_clock.SecondsSinceEpoch = 530;
		_sut.RecordNow(start5); // record a 5s duration
		// original measurement has become stale
		AssertMeasurements(9);
	}

	void AssertMeasurements(double expectedValue) {
		_listener.Observe();

		Assert.Collection(
			_listener.RetrieveMeasurements("the-metric-seconds"),
			m => {
				Assert.Equal(expectedValue, m.Value);
				Assert.Collection(
					m.Tags.ToArray(),
					t => {
						Assert.Equal("name", t.Key);
						Assert.Equal("the-tracker", t.Value);
					},
					t => {
						Assert.Equal("range", t.Key);
						Assert.Equal("16-20 seconds", t.Value);
					});
			});
	}

	[Fact]
	public void no_name() {
		using var meter = new Meter($"{typeof(DurationMaxTrackerTests)}");
		using var listener = new TestMeterListener<double>(meter);
		var sut = new DurationMaxTracker(
			metric: new DurationMaxMetric(meter, "the-metric", legacyNames: false),
			name: null,
			expectedScrapeIntervalSeconds: 15);

		listener.Observe();

		Assert.Collection(
			listener.RetrieveMeasurements("the-metric-seconds"),
			m => {
				Assert.Equal(0, m.Value);
				Assert.Collection(
					m.Tags.ToArray(),
					t => {
						Assert.Equal("range", t.Key);
						Assert.Equal("16-20 seconds", t.Value);
					});
			});
	}
}
