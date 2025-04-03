// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
