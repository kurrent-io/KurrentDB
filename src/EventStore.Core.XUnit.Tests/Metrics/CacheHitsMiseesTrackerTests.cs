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

using static EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.XUnit.Tests.Metrics;

public sealed class CacheHitsMissesTrackerTests : IDisposable {
	private Scope _disposables = new();

	public void Dispose() {
		_disposables.Dispose();
	}

	private (CacheHitsMissesTracker, TestMeterListener<long>) GenSut(
		Cache[] enabledCaches,
		[CallerMemberName] string callerName = "") {

		var meter = new Meter($"{typeof(CacheHitsMissesTrackerTests)}-{callerName}");
		_disposables.RegisterForDispose(meter);

		var listener = new TestMeterListener<long>(meter);
		_disposables.RegisterForDispose(listener);

		var metric = new CacheHitsMissesMetric(meter, enabledCaches, "the-metric", new() {
			{ Cache.StreamInfo, "stream-info" },
			{ Cache.Chunk, "chunk" },
		});
		var sut =  new CacheHitsMissesTracker(metric);
		return (sut, listener);
	}

	[Fact]
	public void observes_all_caches() {
		var (sut, listener) = GenSut(new[] { Cache.Chunk, Cache.StreamInfo });
		sut.Register(Cache.Chunk, () => 1, () => 2);
		sut.Register(Cache.StreamInfo, () => 3, () => 4);
		AssertMeasurements(listener,
			AssertMeasurement("chunk", "hits", 1),
			AssertMeasurement("chunk", "misses", 2),
			AssertMeasurement("stream-info", "hits", 3),
			AssertMeasurement("stream-info", "misses", 4));
	}

	[Fact]
	public void ignores_disabled_cache() {
		var (sut, listener) = GenSut(new[] { Cache.StreamInfo });
		sut.Register(Cache.Chunk, () => 1, () => 2);
		sut.Register(Cache.StreamInfo, () => 3, () => 4);
		AssertMeasurements(listener,
			AssertMeasurement("stream-info", "hits", 3),
			AssertMeasurement("stream-info", "misses", 4));
	}

	[Fact]
	public void ignores_unregistered_cache() {
		var (sut, listener) = GenSut(new[] { Cache.Chunk, Cache.StreamInfo });
		sut.Register(Cache.Chunk, () => 1, () => 2);
		AssertMeasurements(listener,
			AssertMeasurement("chunk", "hits", 1),
			AssertMeasurement("chunk", "misses", 2));
	}

	static Action<TestMeterListener<long>.TestMeasurement> AssertMeasurement(
		string cacheName,
		string kind,
		long expectedValue) =>

		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
			Assert.Collection(
				actualMeasurement.Tags.ToArray(),
				tag => {
					Assert.Equal("cache", tag.Key);
					Assert.Equal(cacheName, tag.Value);
				},
				tag => {
					Assert.Equal("kind", tag.Key);
					Assert.Equal(kind, tag.Value);
				});
		};

	static void AssertMeasurements(
		TestMeterListener<long> listener,
		params Action<TestMeterListener<long>.TestMeasurement>[] actions) {

		listener.Observe();
		Assert.Collection(listener.RetrieveMeasurements("the-metric"), actions);
	}
}
