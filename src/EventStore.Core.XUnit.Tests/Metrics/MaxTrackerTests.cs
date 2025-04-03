// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class MaxTrackerTests : IDisposable {
	private readonly TestMeterListener<long> _listener;
	private readonly FakeClock _clock = new();
	private readonly MaxTracker<long> _sut;

	public MaxTrackerTests() {
		var meter = new Meter($"{typeof(MaxTrackerTests)}");
		_listener = new TestMeterListener<long>(meter);
		var metric = new MaxMetric<long>(meter, "the-metric");
		_sut = new MaxTracker<long>(
			metric: metric,
			name: "the-tracker",
			expectedScrapeIntervalSeconds: 15,
			clock: _clock);
	}

	public void Dispose() {
		_listener.Dispose();
	}

	[Fact]
	public void no_records() {
		AssertMeasurements(0);
	}

	[Fact]
	public void two_records_ascending() {
		AssertMeasurements(0);
		_sut.Record(1);
		AssertMeasurements(1);
		_sut.Record(2);
		AssertMeasurements(2);
	}

	[Fact]
	public void two_records_descending() {
		AssertMeasurements(0);
		_sut.Record(2);
		AssertMeasurements(2);
		_sut.Record(1);
		AssertMeasurements(2);
	}

	[Fact]
	public void removes_stale_data() {
		_sut.Record(1);
		_clock.AdvanceSeconds(19);
		AssertMeasurements(1);

		_clock.AdvanceSeconds(1);
		AssertMeasurements(0);
	}

	void AssertMeasurements(double expectedValue) {
		_listener.Observe();

		Assert.Collection(
			_listener.RetrieveMeasurements("the-metric"),
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
		using var meter = new Meter($"{typeof(MaxTrackerTests)}");
		using var listener = new TestMeterListener<long>(meter);
		var sut = new MaxTracker<long>(
			metric: new MaxMetric<long>(meter, "the-metric"),
			name: null,
			expectedScrapeIntervalSeconds: 15);

		listener.Observe();

		Assert.Collection(
			listener.RetrieveMeasurements("the-metric"),
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
