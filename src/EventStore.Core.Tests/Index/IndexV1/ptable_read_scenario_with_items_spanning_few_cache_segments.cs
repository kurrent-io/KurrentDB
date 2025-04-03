// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false, 10)]
[TestFixture(PTableVersions.IndexV2, true, 10)]
[TestFixture(PTableVersions.IndexV3, false, 10)]
[TestFixture(PTableVersions.IndexV3, true, 10)]
[TestFixture(PTableVersions.IndexV4, false, 10)]
[TestFixture(PTableVersions.IndexV4, true, 10)]
public class
	searching_ptable_with_items_spanning_few_cache_segments_and_all_items_in_cache :
		ptable_read_scenario_with_items_spanning_few_cache_segments {
	public searching_ptable_with_items_spanning_few_cache_segments_and_all_items_in_cache(byte ptableVersion,
		bool skipIndexVerify, int midpointCacheDepth)
		: base(ptableVersion, skipIndexVerify, midpointCacheDepth) {
	}
}

[TestFixture(PTableVersions.IndexV2, false, 0)]
[TestFixture(PTableVersions.IndexV2, true, 0)]
[TestFixture(PTableVersions.IndexV3, false, 0)]
[TestFixture(PTableVersions.IndexV3, true, 0)]
[TestFixture(PTableVersions.IndexV4, false, 0)]
[TestFixture(PTableVersions.IndexV4, true, 0)]
public class
	searching_ptable_with_items_spanning_few_cache_segments_and_only_some_items_in_cache :
		ptable_read_scenario_with_items_spanning_few_cache_segments {
	public searching_ptable_with_items_spanning_few_cache_segments_and_only_some_items_in_cache(byte ptableVersion,
		bool skipIndexVerify, int midpointCacheDepth)
		: base(ptableVersion, skipIndexVerify, midpointCacheDepth) {
	}
}

public abstract class ptable_read_scenario_with_items_spanning_few_cache_segments : PTableReadScenario {
	protected ptable_read_scenario_with_items_spanning_few_cache_segments(byte ptableVersion, bool skipIndexVerify,
		int midpointCacheDepth)
		: base(ptableVersion, skipIndexVerify, midpointCacheDepth) {
	}

	protected override void AddItemsForScenario(IMemTable memTable) {
		memTable.Add(0x010100000000, 0, 0x0001);
		memTable.Add(0x010100000000, 0, 0x0002);
		memTable.Add(0x010500000000, 0, 0x0003);
		memTable.Add(0x010500000000, 0, 0x0004);
		memTable.Add(0x010500000000, 0, 0x0005);
	}

	private ulong GetHash(ulong value) {
		return _ptableVersion == PTableVersions.IndexV1 ? value >> 32 : value;
	}

	[Test]
	public void the_table_has_five_items() {
		Assert.AreEqual(5, PTable.Count);
	}

	[Test]
	public void the_smallest_items_can_be_found() {
		long position;
		Assert.IsTrue(PTable.TryGetOneValue(0x010100000000, 0, out position));
		Assert.AreEqual(0x0002, position);
	}

	[Test]
	public void the_smallest_items_are_returned_in_descending_order() {
		var entries = PTable.GetRange(0x010100000000, 0, 0).ToArray();
		Assert.AreEqual(2, entries.Length);
		Assert.AreEqual(GetHash(0x010100000000), entries[0].Stream);
		Assert.AreEqual(0, entries[0].Version);
		Assert.AreEqual(0x0002, entries[0].Position);
		Assert.AreEqual(GetHash(0x010100000000), entries[1].Stream);
		Assert.AreEqual(0, entries[1].Version);
		Assert.AreEqual(0x0001, entries[1].Position);
	}

	[Test]
	public void try_get_latest_entry_for_smallest_hash_returns_correct_index_entry() {
		IndexEntry entry;
		Assert.IsTrue(PTable.TryGetLatestEntry(0x010100000000, out entry));
		Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0002, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_for_smallest_hash_returns_correct_index_entry() {
		IndexEntry entry;
		Assert.IsTrue(PTable.TryGetOldestEntry(0x010100000000, out entry));
		Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0001, entry.Position);
	}

	[Test]
	public void the_largest_items_can_be_found() {
		long position;
		Assert.IsTrue(PTable.TryGetOneValue(0x010500000000, 0, out position));
		Assert.AreEqual(0x0005, position);
	}

	[Test]
	public void the_largest_items_are_returned_in_descending_order() {
		var entries = PTable.GetRange(0x010500000000, 0, 0).ToArray();
		Assert.AreEqual(3, entries.Length);
		Assert.AreEqual(GetHash(0x010500000000), entries[0].Stream);
		Assert.AreEqual(0, entries[0].Version);
		Assert.AreEqual(0x0005, entries[0].Position);
		Assert.AreEqual(GetHash(0x010500000000), entries[1].Stream);
		Assert.AreEqual(0, entries[1].Version);
		Assert.AreEqual(0x0004, entries[1].Position);
		Assert.AreEqual(GetHash(0x010500000000), entries[2].Stream);
		Assert.AreEqual(0, entries[2].Version);
		Assert.AreEqual(0x0003, entries[2].Position);
	}

	[Test]
	public void try_get_latest_entry_for_largest_hash_returns_correct_index_entry() {
		IndexEntry entry;
		Assert.IsTrue(PTable.TryGetLatestEntry(0x010500000000, out entry));
		Assert.AreEqual(GetHash(0x010500000000), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0005, entry.Position);
	}

	[Test]
	public void try_get_oldest_entry_for_largest_hash_returns_correct_index_entry() {
		IndexEntry entry;
		Assert.IsTrue(PTable.TryGetOldestEntry(0x010500000000, out entry));
		Assert.AreEqual(GetHash(0x010500000000), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0003, entry.Position);
	}

	[Test]
	public void non_existent_item_cannot_be_found() {
		long position;
		Assert.IsFalse(PTable.TryGetOneValue(2, 0, out position));
	}

	[Test]
	public void range_query_returns_nothing_for_nonexistent_stream() {
		var entries = PTable.GetRange(0x010200000000, 0, long.MaxValue).ToArray();
		Assert.AreEqual(0, entries.Length);
	}

	[Test]
	public void try_get_latest_entry_returns_nothing_for_nonexistent_stream() {
		IndexEntry entry;
		Assert.IsFalse(PTable.TryGetLatestEntry(0x010200000000, out entry));
	}

	[Test]
	public void try_get_oldest_entry_returns_nothing_for_nonexistent_stream() {
		IndexEntry entry;
		Assert.IsFalse(PTable.TryGetOldestEntry(0x010200000000, out entry));
	}
}
