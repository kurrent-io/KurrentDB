// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Index;
using EventStore.Core.Metrics;
using EventStore.Core.XUnit.Tests.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Index;

public class IndexStatusTrackerTests : IDisposable {
	private readonly TestMeterListener<long> _listener;
	private readonly FakeClock _clock = new();
	private readonly StatusMetric _metric;
	private readonly IndexStatusTracker _sut;

	public IndexStatusTrackerTests() {
		var meter = new Meter($"{typeof(IndexStatusTrackerTests)}");
		_listener = new TestMeterListener<long>(meter);
		_metric = new StatusMetric(
			meter,
			"eventstore-statuses",
			legacyNames: false,
			_clock);
		_sut = new IndexStatusTracker(_metric);
	}

	public void Dispose() {
		_listener.Dispose();
	}

	[Fact]
	public void can_observe_opening() {
		_clock.SecondsSinceEpoch = 500;
		AssertMeasurements("Idle", 500);

		using (_sut.StartOpening()) {
			_clock.SecondsSinceEpoch = 501;
			AssertMeasurements("Opening", 501);
		}

		_clock.SecondsSinceEpoch = 502;
		AssertMeasurements("Idle", 502);
	}

	[Fact]
	public void can_observe_rebuilding() {
		_clock.SecondsSinceEpoch = 500;
		AssertMeasurements("Idle", 500);

		using (_sut.StartRebuilding()) {
			_clock.SecondsSinceEpoch = 501;
			AssertMeasurements("Rebuilding", 501);
		}

		_clock.SecondsSinceEpoch = 502;
		AssertMeasurements("Idle", 502);
	}

	[Fact]
	public void can_observe_initializing() {
		_clock.SecondsSinceEpoch = 500;
		AssertMeasurements("Idle", 500);

		using (_sut.StartInitializing()) {
			_clock.SecondsSinceEpoch = 501;
			AssertMeasurements("Initializing", 501);
		}

		_clock.SecondsSinceEpoch = 502;
		AssertMeasurements("Idle", 502);
	}

	[Fact]
	public void can_observe_merging() {
		_clock.SecondsSinceEpoch = 500;
		AssertMeasurements("Idle", 500);

		using (_sut.StartMerging()) {
			_clock.SecondsSinceEpoch = 501;
			AssertMeasurements("Merging", 501);
		}

		_clock.SecondsSinceEpoch = 502;
		AssertMeasurements("Idle", 502);
	}

	[Fact]
	public void can_observe_scavenging() {
		_clock.SecondsSinceEpoch = 500;
		AssertMeasurements("Idle", 500);

		using (_sut.StartScavenging()) {
			_clock.SecondsSinceEpoch = 501;
			AssertMeasurements("Scavenging", 501);
		}

		_clock.SecondsSinceEpoch = 502;
		AssertMeasurements("Idle", 502);
	}

	void AssertMeasurements(string expectedStatus, int expectedValue) {
		_listener.Observe();

		Assert.Collection(
			_listener.RetrieveMeasurements("eventstore-statuses"),
			m => {
				Assert.Equal(expectedValue, m.Value);
				Assert.Collection(
					m.Tags.ToArray(),
					t => {
						Assert.Equal("name", t.Key);
						Assert.Equal("Index", t.Value);
					},
					t => {
						Assert.Equal("status", t.Key);
						Assert.Equal(expectedStatus, t.Value);
					});
			});
	}
}
