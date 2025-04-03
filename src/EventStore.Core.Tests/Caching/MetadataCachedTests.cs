// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EventStore.Core.Caching;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using NUnit.Framework;
using MetadataCached = EventStore.Core.Services.Storage.ReaderIndex.IndexBackend<string>.MetadataCached;

namespace EventStore.Core.Tests.Caching;

public class MetadataCachedTests {
	private static MetadataCached CreateMetadataCachedObject() =>
		new(0, new StreamMetadata(
			maxCount: 10,
			maxAge: new TimeSpan(10),
			truncateBefore: null,
			tempStream: false,
			cacheControl: null,
			acl: new StreamAcl(
				new[] { new string(' ', 1), new string(' ', 2) },
				new[] { new string(' ', 0) },
				null,
				null,
				new[] { new string(' ', 1), new string(' ', 3), new string(' ', 3), new string(' ', 7) }
			)));

	[Test]
	public void size_is_measured_correctly() {
		var mem = MemUsage.Calculate(() =>
			new[] { // we need an array to force an allocation
				CreateMetadataCachedObject()
			}, out var metadata);

		Assert.AreEqual(mem, metadata[0].ApproximateSize + MemSizer.ArraySize);
	}

	[Test]
	public void size_in_lru_cache_is_measured_correctly_with_string_key() {
		var lruCache = new LRUCache<string, MetadataCached>(string.Empty, 1, (_,_) => 0);

		// initialize any underlying data structures (the dictionary in this case)
		lruCache.Put("test", CreateMetadataCachedObject());

		var mem = MemUsage.Calculate(() =>
			lruCache.Put(new string('x', 10), CreateMetadataCachedObject()));

		// internally, dictionary items are initialized in batches of ~4
		// to keep the test simple, we just subtract the dictionary entry size
		// to account for this already initialized entry.
		var metadataCached = CreateMetadataCachedObject();
		Assert.AreEqual(mem,
			LRUCache<string, MetadataCached>.ApproximateItemSize(
				MemSizer.SizeOf(new string('x', 10)), metadataCached.ApproximateSize - Unsafe.SizeOf<MetadataCached>()) -
			MemSizer.SizeOfDictionaryEntry<string, LinkedListNode<MetadataCached>>());
	}

	[Test]
	public void size_in_lru_cache_is_measured_correctly_with_long_key() {
		var lruCache = new LRUCache<long, MetadataCached>(string.Empty, 1, (_,_) => 0);

		// initialize any underlying data structures (the dictionary in this case)
		lruCache.Put(123, CreateMetadataCachedObject());

		var mem = MemUsage.Calculate(() =>
			lruCache.Put(456, CreateMetadataCachedObject()));

		// internally, dictionary items are initialized in batches of ~4
		// to keep the test simple, we just subtract the dictionary entry size
		// to account for this already initialized entry.
		var metadataCached = CreateMetadataCachedObject();
		Assert.AreEqual(mem,
			LRUCache<string, MetadataCached>.ApproximateItemSize(0, metadataCached.ApproximateSize - Unsafe.SizeOf<MetadataCached>()) -
			MemSizer.SizeOfDictionaryEntry<string, LinkedListNode<MetadataCached>>());
	}
}
