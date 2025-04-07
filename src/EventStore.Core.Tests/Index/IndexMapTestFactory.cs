// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Index;

namespace EventStore.Core.Tests.Index;

public static class IndexMapTestFactory {
	public static IndexMap FromFile(string filename, int maxTablesPerLevel = 4,
		bool loadPTables = true,
		int cacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000,
		int threads = 1,
		int maxAutoMergeLevel = int.MaxValue,
		int pTableMaxReaderCount = Constants.PTableMaxReaderCountDefault) {
		return IndexMap.FromFile(filename, maxTablesPerLevel, loadPTables, cacheDepth, skipIndexVerify, useBloomFilter, lruCacheSize, threads,
			maxAutoMergeLevel, pTableMaxReaderCount);
	}
}
