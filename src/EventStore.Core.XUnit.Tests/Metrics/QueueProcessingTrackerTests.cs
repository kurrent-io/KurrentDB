// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using EventStore.Core.Metrics;
using EventStore.Core.Time;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class QueueProcessingTrackerTests : IDisposable {
	private readonly TestMeterListener<double> _listener;
	private readonly FakeClock _clock = new();
	private readonly QueueProcessingTracker _sut;

	public QueueProcessingTrackerTests() {
		var meter = new Meter($"{typeof(QueueProcessingTrackerTests)}");
		var metric = new DurationMetric(meter, "the-metric", legacyNames: false, _clock);
		_listener = new TestMeterListener<double>(meter);
		_sut = new(metric, "the-queue");
	}

	public void Dispose() {
		_listener.Dispose();
	}

	[Fact]
	public void records() {
		_clock.SecondsSinceEpoch = 500;
		var start = _clock.Now;
		_clock.SecondsSinceEpoch = 501;
		var end = _sut.RecordNow(start, "the-message-type");
		var elapsedSeconds = end.ElapsedSecondsSince(start);
		Assert.Equal(1.000, elapsedSeconds);
		AssertMeasurements(1);
	}

	void AssertMeasurements(int expectedValue) {

		Assert.Collection(
			_listener.RetrieveMeasurements("the-metric-seconds"),
			m => {
				Assert.Equal(expectedValue, m.Value);
				Assert.Collection(
					m.Tags,
					t => {
						Assert.Equal("queue", t.Key);
						Assert.Equal("the-queue", t.Value);
					},
					t => {
						Assert.Equal("message-type", t.Key);
						Assert.Equal("the-message-type", t.Value);
					});
			});
	}
}

public class QueueProcessingTrackerNoOpTests {
	// the noop tracker doesn't track anything but it still needs to return the current time
	[Fact]
	public async Task noop_returns_current_time() {
		var sut = new QueueProcessingTracker.NoOp();
		var start = Instant.Now;
		await Task.Delay(1);
		var end = sut.RecordNow(start, "some message type");
		Assert.True(start < end);
	}
}
