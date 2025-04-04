// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using EventStore.Core.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class QueueBusyTrackerTests {
	[Fact]
	public async Task records() {
		using var meter = new Meter($"{typeof(QueueProcessingTrackerTests)}");
		using var listener = new TestMeterListener<double>(meter);
		var metric = new AverageMetric(meter, "the-metric", "seconds", label => new("queue", label), legacyNames: false);
		var sut = new QueueBusyTracker(metric, "the-queue");

		sut.EnterBusy();
		await Task.Delay(1);
		sut.EnterIdle();
		listener.Observe();

		Assert.Collection(
			listener.RetrieveMeasurements("the-metric-seconds"),
			m => {
				Assert.True(m.Value > 0.0001);
				Assert.Collection(
					m.Tags,
					t => {
						Assert.Equal("queue", t.Key);
						Assert.Equal("the-queue", t.Value);
					});
			});
	}
}
