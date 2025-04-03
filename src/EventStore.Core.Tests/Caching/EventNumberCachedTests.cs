// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Caching;
using EventStore.Core.DataStructures;
using NUnit.Framework;
using EventNumberCached = EventStore.Core.Services.Storage.ReaderIndex.IndexBackend<string>.EventNumberCached;

namespace EventStore.Core.Tests.Caching;

public class EventNumberCachedTests {
	[Test]
	public void size_is_measured_correctly() {
		var mem = MemUsage.Calculate(() =>
			new EventNumberCached[] { // we need an array to force an allocation
				new(10, 123)
			}, out _);

		Assert.AreEqual(mem, EventNumberCached.ApproximateSize + MemSizer.ArraySize);
	}

	[Test]
	public void size_in_lru_cache_is_measured_correctly_with_string_key() {
		var lruCache = new LRUCache<string, EventNumberCached>(string.Empty, 1, (_,_) => 0);

		// initialize any underlying data structures (the dictionary in this case)
		lruCache.Put("test", new EventNumberCached(0, 0));

		var mem = MemUsage.Calculate(() =>
			lruCache.Put(new string('x', 10), new EventNumberCached(10, 23)));

		// internally, dictionary items are initialized in batches of ~4
		// to keep the test simple, we just subtract the dictionary entry size
		// to account for this already initialized entry.
		Assert.AreEqual(mem,
			LRUCache<string, EventNumberCached>.ApproximateItemSize(
				MemSizer.SizeOf(new string('x', 10)), 0) -
			MemSizer.SizeOfDictionaryEntry<string, LinkedListNode<EventNumberCached>>());
	}

	[Test]
	public void size_in_lru_cache_is_measured_correctly_with_long_key() {
		var lruCache = new LRUCache<long, EventNumberCached>(string.Empty, 1, (_,_) => 0);

		// initialize any underlying data structures (the dictionary in this case)
		lruCache.Put(123, new EventNumberCached(0, 0));

		var mem = MemUsage.Calculate(() =>
			lruCache.Put(456, new EventNumberCached(10, 123)));

		// internally, dictionary items are initialized in batches of ~4
		// to keep the test simple, we just subtract the dictionary entry size
		// to account for this already initialized entry.
		Assert.AreEqual(mem,
			LRUCache<string, EventNumberCached>.ApproximateItemSize(0, 0) -
			MemSizer.SizeOfDictionaryEntry<long, LinkedListNode<EventNumberCached>>());
	}
}
