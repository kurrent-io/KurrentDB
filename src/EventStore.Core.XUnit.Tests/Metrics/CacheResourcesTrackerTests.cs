// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.CompilerServices;
using DotNext.Runtime.CompilerServices;
using EventStore.Core.Metrics;
using EventStore.Core.Tests;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public sealed class CacheResourcesTrackerTests : IDisposable {
	private Scope _disposables = new();

	public void Dispose() {
		_disposables.Dispose();
	}

	private (CacheResourcesTracker, TestMeterListener<long>) GenSut(
		[CallerMemberName] string callerName = "") {

		var meter = new Meter($"{typeof(CacheResourcesTrackerTests)}-{callerName}");
		_disposables.RegisterForDispose(meter);

		var listener = new TestMeterListener<long>(meter);
		_disposables.RegisterForDispose(listener);

		var metrics = new CacheResourcesMetrics(meter, "the-metric", legacyNames: false);
		var sut =  new CacheResourcesTracker(metrics);
		return (sut, listener);
	}

	[Fact]
	public void observes_all_caches() {
		var (sut, listener) = GenSut();

		sut.Register("cacheA", Caching.ResizerUnit.Entries, () => new("", "",
			capacity: 1,
			size: 2,
			count: 3,
			numChildren: 0));

		sut.Register("cacheB", Caching.ResizerUnit.Bytes, () => new("", "",
			capacity: 4,
			size: 5,
			count: 6,
			numChildren: 0));

		listener.Observe();
		// here we look up -entries-entries, but both the exporters (Prometheus and OTEL) deduplicate
		// the fact that the metric name ends with the unit. we avoid having two metrics with the same
		// name but different units.
		AssertMeasurements(listener, "the-metric-entries-entries",
			AssertMeasurement("cacheA", "capacity", 1),
			AssertMeasurement("cacheA", "size", 2),
			AssertMeasurement("cacheA", "count", 3),
			AssertMeasurement("cacheB", "count", 6));

		AssertMeasurements(listener, "the-metric-bytes-bytes",
			AssertMeasurement("cacheB", "capacity", 4),
			AssertMeasurement("cacheB", "size", 5));
	}

	static Action<TestMeterListener<long>.TestMeasurement> AssertMeasurement(
		string cacheKey,
		string kind,
		long expectedValue) =>

		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
			Assert.Collection(
				actualMeasurement.Tags.ToArray(),
				tag => {
					Assert.Equal("cache", tag.Key);
					Assert.Equal(cacheKey, tag.Value);
				},
				tag => {
					Assert.Equal("kind", tag.Key);
					Assert.Equal(kind, tag.Value);
				});
		};

	static void AssertMeasurements(
		TestMeterListener<long> listener,
		string metric,
		params Action<TestMeterListener<long>.TestMeasurement>[] actions) {

		Assert.Collection(listener.RetrieveMeasurements(metric), actions);
	}
}
