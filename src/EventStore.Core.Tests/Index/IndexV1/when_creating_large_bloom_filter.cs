// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.DataStructures.ProbabilisticFilter;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

public class when_creating_large_bloom_filter : SpecificationWithDirectory {
	[Ignore("Quick but requires 4gb disk")]
	[Test]
	public void ptable_exceeding_maximum_filter_size_succeeds() {
		var file = GetTempFilePath();

		// create
		var filter = PTable.ConstructBloomFilter(
			useBloomFilter: true,
			filename: file,
			indexEntryCount: 16_384_000_000);
		filter.Dispose();

		// and reopen
		filter = new PersistentBloomFilter(FileStreamPersistence.FromFile(
			PTable.GenBloomFilterFilename(file)));
		filter.Dispose();
	}

	[Test]
	public void out_of_memory_returns_null() {
		var filter = PTable.ConstructBloomFilter(
			useBloomFilter: true,
			filename: GetTempFilePath(),
			indexEntryCount: 1,
			genBloomFilterSizeBytes: _ => 1_000_000_000_000); // rly big

		Assert.Null(filter);
	}
}
