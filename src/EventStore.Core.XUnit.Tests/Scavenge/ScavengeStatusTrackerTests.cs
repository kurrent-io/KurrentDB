// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Metrics;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.XUnit.Tests.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ScavengeStatusTrackerTests : IDisposable {
	private readonly TestMeterListener<long> _listener;
	private readonly FakeClock _clock = new();
	private readonly StatusMetric _metric;
	private readonly ScavengeStatusTracker _sut;

	public ScavengeStatusTrackerTests() {
		var meter = new Meter($"{typeof(ScavengeStatusTrackerTests)}");
		_listener = new TestMeterListener<long>(meter);
		_metric = new StatusMetric(
			meter,
			"eventstore-statuses",
			legacyNames: false,
			_clock);
		_sut = new ScavengeStatusTracker(_metric);
	}

	public void Dispose() {
		_listener?.Dispose();
	}

	[Fact]
	public void can_observe_activity() {
		_clock.SecondsSinceEpoch = 500;
		AssertMeasurements("Idle", 500);

		using (_sut.StartActivity("Accumulation")) {
			_clock.SecondsSinceEpoch = 501;
			AssertMeasurements("Accumulation Phase", 501);
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
						Assert.Equal("Scavenge", t.Value);
					},
					t => {
						Assert.Equal("status", t.Key);
						Assert.Equal(expectedStatus, t.Value);
					});
			});
	}
}
