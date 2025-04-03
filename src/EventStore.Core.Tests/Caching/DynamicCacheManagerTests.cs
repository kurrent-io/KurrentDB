// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Caching;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.Replication;
using NUnit.Framework;

namespace EventStore.Core.Tests.Caching;

[TestFixture]
public class DynamicCacheManagerTests {
	private readonly FakePublisher _fakePublisher = new();

	private DynamicCacheManager GenSut(
		Func<long> getFreeSystemMem,
		Func<long> getFreeHeapMem,
		Func<int> getGcCollectionCount,
		long totalMem,
		int keepFreeMemPercent,
		long keepFreeMemBytes,
		TimeSpan monitoringInterval,
		TimeSpan minResizeInterval,
		long minResizeThreshold,
		ICacheResizer rootCacheResizer) {
		var sut = new DynamicCacheManager(
			_fakePublisher,
			getFreeSystemMem,
			getFreeHeapMem,
			getGcCollectionCount,
			totalMem,
			keepFreeMemPercent,
			keepFreeMemBytes,
			monitoringInterval,
			minResizeInterval,
			minResizeThreshold,
			rootCacheResizer,
			new CacheResourcesTracker.NoOp());
		sut.Start();

		_fakePublisher.Messages.Clear();

		return sut;
	}

	private async Task TickPublished() {
		while (!_fakePublisher.Messages.ToArray().Any(x => x is TimerMessage.Schedule))
			await Task.Delay(10);

		_fakePublisher.Messages.Clear();
	}

	[Test]
	public async Task ticks() {
		var sut = GenSut(
			() => 100,
			() => 0,
			() => 0,
			100,
			0,
			0,
			TimeSpan.MaxValue,
			TimeSpan.MaxValue,
			0,
			new StaticCacheResizer(ResizerUnit.Bytes, 0, EmptyDynamicCache.Instance));

		sut.Handle(new MonitoringMessage.DynamicCacheManagerTick());
		await TickPublished().WithTimeout(TimeSpan.FromSeconds(10));
	}

	[TestCase(20, 0, true)]
	[TestCase(0, 20, true)]
	[TestCase(20, 0, false)]
	[TestCase(0, 20, false)]
	public async Task caches_resized_depending_on_whether_total_free_memory_is_above_or_below_keep_free_mem(int percent, long bytes, bool aboveKeepFreeMem) {
		long cache1Mem = -1, cache2Mem = -1;
		var cache1 = new DynamicCacheResizer(ResizerUnit.Bytes, 1, 100_000, 60, new AdHocDynamicCache(
			() => 0,
			mem => Interlocked.Exchange(ref cache1Mem, mem),
			() => 1, // included in total free mem
			() => { },
			name: "cache1"));

		var cache2 = new DynamicCacheResizer(ResizerUnit.Bytes, 2, 100_000, 40, new AdHocDynamicCache(
			() => 0,
			mem => Interlocked.Exchange(ref cache2Mem, mem),
			() => 1, // included in total free mem
			() => { },
			name: "cache2"));

		var freeSystemMemReq = 0;
		var freeSystemMem = new[] { 100, 11 + (aboveKeepFreeMem ? 1 : 0)}; // included in total free mem

		var freeHeapMemReq = 0;
		var freeHeapMem = new[] { 0, 6 }; // included in total free mem

		var sut = GenSut(
			() => freeSystemMem[freeSystemMemReq++],
			() => freeHeapMem[freeHeapMemReq++],
			() => 0,
			100,
			percent,
			bytes,
			TimeSpan.MaxValue,
			TimeSpan.MaxValue,
			0,
			new CompositeCacheResizer("root", 100, cache1, cache2));

		sut.Handle(new MonitoringMessage.DynamicCacheManagerTick());

		await TickPublished();

		if (aboveKeepFreeMem) {
			// caches not resized to minimum amount
			Assert.AreNotEqual(1, Interlocked.Read(ref cache1Mem));
			Assert.AreNotEqual(2, Interlocked.Read(ref cache2Mem));
		} else {
			// caches resized to minimum amount
			Assert.AreEqual(1, Interlocked.Read(ref cache1Mem));
			Assert.AreEqual(2, Interlocked.Read(ref cache2Mem));
		}
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task caches_resized_depending_on_whether_min_resize_interval_was_exceeded(bool minResizeIntervalExceeded) {
		long cache1Mem = -1, cache2Mem = -1;
		var cache1 = new DynamicCacheResizer(ResizerUnit.Bytes, 1, 100_000, 60, new AdHocDynamicCache(
			() => 0,
			mem => Interlocked.Exchange(ref cache1Mem, mem), name: "cache1"));
		var cache2 = new DynamicCacheResizer(ResizerUnit.Bytes, 2, 100_000, 40, new AdHocDynamicCache(
			() => 0,
			mem => Interlocked.Exchange(ref cache2Mem, mem), name: "cache2"));

		var request = 0;
		var freeMem = new[] { 100, 90 };

		var sut = GenSut(
			() => freeMem[request++],
			() => 0,
			() => 0,
			100,
			0,
			0,
			TimeSpan.MaxValue,
			minResizeIntervalExceeded ? TimeSpan.Zero : TimeSpan.MaxValue,
			0,
			new CompositeCacheResizer("root", 100, cache1, cache2));

		sut.Handle(new MonitoringMessage.DynamicCacheManagerTick());

		await TickPublished();

		if (minResizeIntervalExceeded) {
			// caches resized according to 90% free memory
			Assert.AreEqual(54, Interlocked.Read(ref cache1Mem));
			Assert.AreEqual(36, Interlocked.Read(ref cache2Mem));
		} else {
			// caches not resized
			Assert.AreNotEqual(54, Interlocked.Read(ref cache1Mem));
			Assert.AreNotEqual(36, Interlocked.Read(ref cache2Mem));
		}
	}

	[TestCase(20, 0, true)]
	[TestCase(0, 20, false)]
	public async Task caches_resized_depending_on_whether_min_resize_threshold_was_exceeded(int percent, long bytes, bool minResizeThresholdExceeded) {
		long cache1Mem = -1, cache2Mem = -1;
		var cache1 = new DynamicCacheResizer(ResizerUnit.Bytes, 1, 100_000, 60, new AdHocDynamicCache(
			() => 0,
			mem => Interlocked.Exchange(ref cache1Mem, mem), name: "cache1"));

		var cache2 = new DynamicCacheResizer(ResizerUnit.Bytes, 2, 100_000, 40, new AdHocDynamicCache(
			() => 0,
			mem => Interlocked.Exchange(ref cache2Mem, mem), name: "cache2"));

		var freeSystemMemReq = 0;
		var freeSystemMem = new[] { 100, 19 };

		var sut = GenSut(
			() => freeSystemMem[freeSystemMemReq++],
			() => 0,
			() => 0,
			100,
			percent,
			bytes,
			TimeSpan.MaxValue,
			TimeSpan.Zero,
			minResizeThresholdExceeded ? 0 : 100,
			new CompositeCacheResizer("root", 100, cache1, cache2));

		sut.Handle(new MonitoringMessage.DynamicCacheManagerTick());

		await TickPublished();

		if (minResizeThresholdExceeded) {
			// caches resized to minimum amount
			Assert.AreEqual(1, Interlocked.Read(ref cache1Mem));
			Assert.AreEqual(2, Interlocked.Read(ref cache2Mem));
		} else {
			// memory below keep free mem and min resize interval is zero
			// but caches not resized to minimum amount due to high min resize threshold (100)
			Assert.AreNotEqual(1, Interlocked.Read(ref cache1Mem));
			Assert.AreNotEqual(2, Interlocked.Read(ref cache2Mem));
		}
	}

	[Test]
	public async Task freed_size_is_reset_when_gc_occurs() {
		var tcs = new TaskCompletionSource<bool>();

		var cache = new AdHocDynamicCache(
			() => 0,
			_ => { },
			() => 0,
			() => tcs.TrySetResult(true));

		var cacheResizer = new DynamicCacheResizer(ResizerUnit.Bytes, 0, 100_000, 100, cache);

		var sut = GenSut(
			() => 100,
			() => 0,
			() => 1,
			100,
			0,
			0,
			TimeSpan.MaxValue,
			TimeSpan.MaxValue,
			0,
			new CompositeCacheResizer("root", 100, cacheResizer));

		sut.Handle(new MonitoringMessage.DynamicCacheManagerTick());
		await TickPublished();

		await tcs.Task.WithTimeout(1000);
		Assert.True(tcs.Task.Result);
	}

	[Test]
	public async Task available_memory_is_recalculated_when_gc_occurs() {
		var cacheCapacity = -1L;

		var cache = new AdHocDynamicCache(
			() => 0,
			x => Interlocked.Exchange(ref cacheCapacity, x));

		var cacheResizer = new DynamicCacheResizer(ResizerUnit.Bytes, 0, 100_000, 100, cache);

		var freeMemReq = 0;
		var freeMem = new [] { 100 /* init */, 99 /* first calculation */, 98 /* second calculation */};

		var gcCountReq = 0;
		var gcCount = new[] { 0 /* init */, 0 /* reset freed size check */, 1 /* first calculation */, 1 /* second calculation */};

		var sut = GenSut(
			() => freeMem[freeMemReq++],
			() => 0,
			() => gcCount[gcCountReq++],
			100,
			20,
			20,
			TimeSpan.MaxValue,
			TimeSpan.Zero,
			0,
			new CompositeCacheResizer("root", 100, cacheResizer));

		sut.Handle(new MonitoringMessage.DynamicCacheManagerTick());
		await TickPublished();

		Assert.AreEqual(78, cacheCapacity);
	}

	[Test]
	public void correct_stats_are_produced() {
		var cache1 = new DynamicCacheResizer(ResizerUnit.Bytes, 10, 100_000, 100, new AdHocDynamicCache(
			() => 12,
			mem => { },
			name: "test1"));
		var cache2 = new StaticCacheResizer(ResizerUnit.Bytes, 15, new AdHocDynamicCache(
			() => 10,
			mem => { },
			name: "test2"));

		var sut = GenSut(
			() => 58,
			() => 0,
			() => 0,
			100,
			0,
			0,
			TimeSpan.MaxValue,
			TimeSpan.MaxValue,
			0,
			new CompositeCacheResizer("cache", 123, cache1, cache2));

		var envelope = new FakeEnvelope();
		sut.Handle(new MonitoringMessage.InternalStatsRequest(envelope));

		Assert.AreEqual(1, envelope.Replies.Count);

		var msg = (MonitoringMessage.InternalStatsRequestResponse) envelope.Replies.First();
		var expectedStats = new Dictionary<string, object> {
			{"es-cache-name", "cache"},
			{"es-cache-sizeBytes", 22L},
			{"es-cache-count", 22L},
			{"es-cache-capacityBytes", 80L},
			{"es-cache-utilizationPercent", 100.0 * 22 / 80},

			{"es-cache-test1-name", "test1"},
			{"es-cache-test1-sizeBytes", 12L},
			{"es-cache-test1-count", 12L},
			{"es-cache-test1-capacityBytes", 65L},
			{"es-cache-test1-utilizationPercent", 100.0 * 12 / 65},

			{"es-cache-test2-name", "test2"},
			{"es-cache-test2-sizeBytes", 10L},
			{"es-cache-test2-count", 10L},
			{"es-cache-test2-capacityBytes", 15L},
			{"es-cache-test2-utilizationPercent", 100.0 * 10 / 15},
		};

		CollectionAssert.AreEquivalent(expectedStats, msg.Stats);
	}
}
