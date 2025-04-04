// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Linq;
using EventStore.Core.Metrics;
using Xunit;
using Conf = EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class IncomingGrpcCallsMetricTests {
	[EventSource(Name = nameof(IncomingGrpcCallsMetricTests))]
	private class TestEventSource : EventSource {
		[Event(1)] public void CallStart() => WriteEvent(1);
		[Event(2)] public void CallStop() => WriteEvent(2);
		[Event(3)] public void CallFailed() => WriteEvent(3);
		[Event(4)] public void CallDeadlineExceeded() => WriteEvent(4);
		[Event(5)] public void CallUnimplemented() => WriteEvent(5);
	}

	[Fact]
	public void can_collect() {
		using var testSource = new TestEventSource();
		using var meter = new Meter($"{typeof(IncomingGrpcCallsMetricTests)}");
		using var listener = new TestMeterListener<long>(meter);
		using var sut = new IncomingGrpcCallsMetric(meter, "current", "totals", new[] {
			Conf.IncomingGrpcCall.Current,
			Conf.IncomingGrpcCall.Total,
			Conf.IncomingGrpcCall.Failed,
			Conf.IncomingGrpcCall.Unimplemented,
			Conf.IncomingGrpcCall.DeadlineExceeded
		});

		sut.EnableEvents(testSource, EventLevel.Verbose);

		listener.Observe();
		AssertMeasurements(listener, "current", AssertCurrentMeasurement(0));
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 0),
			AssertMeasurement("failed", 0),
			AssertMeasurement("unimplemented", 0),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallStart();
		listener.Observe();
		AssertMeasurements(listener, "current", AssertCurrentMeasurement(1));
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("failed", 0),
			AssertMeasurement("unimplemented", 0),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallFailed();
		listener.Observe();
		AssertMeasurements(listener, "current", AssertCurrentMeasurement(1));
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("failed", 1),
			AssertMeasurement("unimplemented", 0),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallUnimplemented();
		listener.Observe();
		AssertMeasurements(listener, "current", AssertCurrentMeasurement(1));
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("failed", 1),
			AssertMeasurement("unimplemented", 1),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallDeadlineExceeded();
		listener.Observe();
		AssertMeasurements(listener, "current", AssertCurrentMeasurement(1));
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("failed", 1),
			AssertMeasurement("unimplemented", 1),
			AssertMeasurement("deadline-exceeded", 1));

		testSource.CallStop();
		listener.Observe();
		AssertMeasurements(listener, "current", AssertCurrentMeasurement(0));
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("failed", 1),
			AssertMeasurement("unimplemented", 1),
			AssertMeasurement("deadline-exceeded", 1));
	}

	[Fact]
	public void can_collect_filtered() {
		using var testSource = new TestEventSource();
		using var meter = new Meter($"{typeof(IncomingGrpcCallsMetricTests)}");
		using var listener = new TestMeterListener<long>(meter);
		using var sut = new IncomingGrpcCallsMetric(meter, "current", "totals", new[] {
			Conf.IncomingGrpcCall.Total,
			Conf.IncomingGrpcCall.DeadlineExceeded
		});

		sut.EnableEvents(testSource, EventLevel.Verbose);
		listener.Observe();
		AssertMeasurements(listener, "current");
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 0),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallStart();
		listener.Observe();
		AssertMeasurements(listener, "current");
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallFailed();
		listener.Observe();
		AssertMeasurements(listener, "current");
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallUnimplemented();
		listener.Observe();
		AssertMeasurements(listener, "current");
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("deadline-exceeded", 0));

		testSource.CallDeadlineExceeded();
		listener.Observe();
		AssertMeasurements(listener, "current");
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("deadline-exceeded", 1));

		testSource.CallStop();
		listener.Observe();
		AssertMeasurements(listener, "current");
		AssertMeasurements(listener, "totals",
			AssertMeasurement("total", 1),
			AssertMeasurement("deadline-exceeded", 1));
	}

	static Action<TestMeterListener<long>.TestMeasurement> AssertCurrentMeasurement(
		long expectedValue) =>

		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
			Assert.Empty(actualMeasurement.Tags);
		};

	static Action<TestMeterListener<long>.TestMeasurement> AssertMeasurement(
		string expectedKind,
		long expectedValue) =>

		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
			Assert.Collection(
				actualMeasurement.Tags.ToArray(),
				tag => {
					Assert.Equal("kind", tag.Key);
					Assert.Equal(expectedKind, tag.Value);
				});
		};

	static void AssertMeasurements(
		TestMeterListener<long> listener,
		string name,
		params Action<TestMeterListener<long>.TestMeasurement>[] actions) {

		Assert.Collection(listener.RetrieveMeasurements(name), actions);
	}
}
