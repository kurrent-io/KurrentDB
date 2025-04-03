// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Caching;
using NUnit.Framework;

namespace EventStore.Core.Tests.Caching;

[TestFixture]
public class CacheResizerTests {
	[Test]
	public void dynamic_resizer_loopback() {
		var cacheResizer = new DynamicCacheResizer(ResizerUnit.Bytes, 10, 100, 12, EmptyDynamicCache.Instance);
		Assert.AreEqual(12, cacheResizer.Weight);
	}

	[Test]
	public void static_resizer_loopback() {
		var cacheResizer = new StaticCacheResizer(ResizerUnit.Bytes, 10, EmptyDynamicCache.Instance);
		Assert.AreEqual(0, cacheResizer.Weight);
	}

	[Test]
	public void dynamic_resizer_with_zero_weight_throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() =>
				new DynamicCacheResizer(ResizerUnit.Bytes, 0, 0, 0, EmptyDynamicCache.Instance));

	[Test]
	public void dynamic_resizer_with_negative_weight_throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new DynamicCacheResizer(ResizerUnit.Bytes, 0, 0, -1, EmptyDynamicCache.Instance));

	[Test]
	public void dynamic_resizer_with_negative_min_capacity_throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new DynamicCacheResizer(ResizerUnit.Bytes, -1, 0, 10, EmptyDynamicCache.Instance));

	[Test]
	public void dynamic_resizer_with_negative_max_capacity_throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new DynamicCacheResizer(ResizerUnit.Bytes, 0, -1, 10, EmptyDynamicCache.Instance));

	[Test]
	public void static_resizer_with_negative_capacity_throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new StaticCacheResizer(ResizerUnit.Bytes, -1, EmptyDynamicCache.Instance));

	[Test]
	public void composite_resizer_with_mixed_units_throws() =>
		Assert.Throws<ArgumentException>(() =>
			new CompositeCacheResizer("root", 100,
				new StaticCacheResizer(ResizerUnit.Bytes, 10, EmptyDynamicCache.Instance),
				new StaticCacheResizer(ResizerUnit.Entries, 10, EmptyDynamicCache.Instance)));

	[Test]
	public void static_calculates_capacity_correctly() {
		var cache = new EmptyDynamicCache();

		var sut = new StaticCacheResizer(ResizerUnit.Bytes, 1000, cache);

		sut.CalcCapacityTopLevel(2000);
		Assert.AreEqual(1000, cache.Capacity);

		sut.CalcCapacityTopLevel(200);
		Assert.AreEqual(1000, cache.Capacity);
	}

	[Test]
	public void dynamic_calculates_capacity_correctly() {
		var cache = new EmptyDynamicCache();

		var sut = new DynamicCacheResizer(ResizerUnit.Bytes, 1000, 100_000, 50, cache);

		sut.CalcCapacityTopLevel(4000);
		Assert.AreEqual(4000, cache.Capacity);

		sut.CalcCapacityTopLevel(200);
		Assert.AreEqual(1000, cache.Capacity);
	}

	[Test]
	public void composite_calculates_capacity_correctly_static() {
		var cacheA = new EmptyDynamicCache();
		var cacheB = new EmptyDynamicCache();

		var sut = new CompositeCacheResizer("root", 100,
			new StaticCacheResizer(ResizerUnit.Bytes, 1000, cacheA),
			new StaticCacheResizer(ResizerUnit.Bytes, 2000, cacheB));

		sut.CalcCapacityTopLevel(4000);
		Assert.AreEqual(1000, cacheA.Capacity);
		Assert.AreEqual(2000, cacheB.Capacity);

		sut.CalcCapacityTopLevel(200);
		Assert.AreEqual(1000, cacheA.Capacity);
		Assert.AreEqual(2000, cacheB.Capacity);
	}

	[Test]
	public void composite_calculates_capacity_correctly_dynamic() {
		var cacheA = new EmptyDynamicCache();
		var cacheB = new EmptyDynamicCache();

		var sut = new CompositeCacheResizer("root", 100,
			new DynamicCacheResizer(ResizerUnit.Bytes, 3000, 100_000, 40, cacheA),
			new DynamicCacheResizer(ResizerUnit.Bytes, 1000, 100_000, 60, cacheB));

		sut.CalcCapacityTopLevel(10_000);
		Assert.AreEqual(4000, cacheA.Capacity);
		Assert.AreEqual(6000, cacheB.Capacity);

		// nb: we overflow the capacity available in order to meet the minimums
		sut.CalcCapacityTopLevel(5000);
		Assert.AreEqual(3000, cacheA.Capacity); // <-- minimum
		Assert.AreEqual(3000, cacheB.Capacity); // <-- 60% of 5000

		sut.CalcCapacityTopLevel(200);
		Assert.AreEqual(3000, cacheA.Capacity);
		Assert.AreEqual(1000, cacheB.Capacity);

	}

	[Test]
	public void composite_calculates_capacity_correctly_mixed() {
		var cacheA = new EmptyDynamicCache();
		var cacheB = new EmptyDynamicCache();

		var sut = new CompositeCacheResizer("root", 100,
			new StaticCacheResizer(ResizerUnit.Bytes, 1000, cacheA),
			new DynamicCacheResizer(ResizerUnit.Bytes, 1000, 100_000, 60, cacheB));

		sut.CalcCapacityTopLevel(10_000);
		Assert.AreEqual(1000, cacheA.Capacity);
		Assert.AreEqual(9000, cacheB.Capacity);
	}

	[Test]
	public void composite_calculates_capacity_correctly_complex() {
		var cacheA = new EmptyDynamicCache();
		var cacheB = new EmptyDynamicCache();
		var cacheC = new EmptyDynamicCache();
		var cacheD = new EmptyDynamicCache();

		// root
		//  -> static 1000                 A
		//  -> composite
		//      -> static 1000             B
		//      -> dynamic 60% min 1000    C
		//      -> dynamic 40% min 1000    D

		var sut = new CompositeCacheResizer(
			name: "root",
			weight: 100,
			new StaticCacheResizer(
				unit: ResizerUnit.Bytes,
				capacity: 1000,
				cache: cacheA),
			new CompositeCacheResizer(
				name: "composite",
				weight: 50,
				new StaticCacheResizer(
					unit: ResizerUnit.Bytes,
					capacity: 1000,
					cache: cacheB),
				new DynamicCacheResizer(
					unit: ResizerUnit.Bytes,
					minCapacity: 1000,
					maxCapacity: 100_000,
					weight: 60,
					cache: cacheC),
				new DynamicCacheResizer(
					unit: ResizerUnit.Bytes,
					minCapacity: 1000,
					maxCapacity: 100_000,
					weight: 40,
					cache: cacheD)));

		// lots of space
		sut.CalcCapacityTopLevel(12_000);

		Assert.AreEqual(1000, cacheA.Capacity);
		Assert.AreEqual(1000, cacheB.Capacity);
		Assert.AreEqual(6000, cacheC.Capacity);
		Assert.AreEqual(4000, cacheD.Capacity);

		// low space
		sut.CalcCapacityTopLevel(1_000);

		Assert.AreEqual(1000, cacheA.Capacity);
		Assert.AreEqual(1000, cacheB.Capacity);
		Assert.AreEqual(1000, cacheC.Capacity);
		Assert.AreEqual(1000, cacheD.Capacity);
	}
}
