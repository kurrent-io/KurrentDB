// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using EventStore.Core.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class AverageMetricTests {
	[Fact]
	public void calculates_average() {
		using var meter = new Meter($"{typeof(QueueProcessingTrackerTests)}");
		using var listener = new TestMeterListener<double>(meter);
		var sut = new AverageMetric(meter, "the-metric", "seconds", label => new("queue", label), legacyNames: false);
		sut.Register("readers", () => 1);
		sut.Register("readers", () => 2);
		sut.Register("writer", () => 3);
		listener.Observe();

		Assert.Collection(
			listener.RetrieveMeasurements("the-metric-seconds"),
			m => {
				Assert.Equal(1.5, m.Value);
				Assert.Collection(
					m.Tags,
					t => {
						Assert.Equal("queue", t.Key);
						Assert.Equal("readers", t.Value);
					});
			},
			m => {
				Assert.Equal(3, m.Value);
				Assert.Collection(
					m.Tags,
					t => {
						Assert.Equal("queue", t.Key);
						Assert.Equal("writer", t.Value);
					});
			});
	}
}
