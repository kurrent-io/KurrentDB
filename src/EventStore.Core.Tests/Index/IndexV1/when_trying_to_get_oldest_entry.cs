// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false, 1_000_000)]
[TestFixture(PTableVersions.IndexV2, true, 1_000_000)]
[TestFixture(PTableVersions.IndexV3, false, 1_000_000)]
[TestFixture(PTableVersions.IndexV3, true, 1_000_000)]
[TestFixture(PTableVersions.IndexV4, false, 1_000_000)]
[TestFixture(PTableVersions.IndexV4, true, 1_000_000)]
[TestFixture(PTableVersions.IndexV4, false, 0)]
[TestFixture(PTableVersions.IndexV4, true, 0)]
public class when_trying_to_get_oldest_entry : SpecificationWithFile {
	protected byte _ptableVersion = PTableVersions.IndexV1;

	private readonly bool _skipIndexVerify;
	private readonly bool _useBloomFilter;
	private readonly int _lruCacheSize;

	public when_trying_to_get_oldest_entry(byte version, bool skipIndexVerify, int lruCacheSize) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
		_useBloomFilter = skipIndexVerify; // bloomfilter orthogonal
		_lruCacheSize = lruCacheSize;
	}

	private ulong GetHash(ulong value) {
		return _ptableVersion == PTableVersions.IndexV1 ? value >> 32 : value;
	}

	private PTable ConstructPTable(HashListMemTable memTable) {
		return PTable.FromMemtable(
			memTable,
			Filename,
			Constants.PTableInitialReaderCount,
			Constants.PTableMaxReaderCountDefault,
			skipIndexVerify: _skipIndexVerify,
			useBloomFilter: _useBloomFilter,
			lruCacheSize: _lruCacheSize);
	}

	[Test]
	public void nothing_is_found_on_empty_stream() {
		var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memTable.Add(0x010100000000, 0x01, 0xffff);
		using (var ptable = ConstructPTable(memTable)) {
			IndexEntry entry;
			Assert.IsFalse(ptable.TryGetOldestEntry(0x12, out entry));
		}
	}

	[Test]
	public void single_item_is_latest() {
		var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memTable.Add(0x010100000000, 0x01, 0xffff);
		using (var ptable = ConstructPTable(memTable)) {
			IndexEntry entry;
			Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
			Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
			Assert.AreEqual(0x01, entry.Version);
			Assert.AreEqual(0xffff, entry.Position);
		}
	}

	[Test]
	public void correct_entry_is_returned() {
		var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memTable.Add(0x010100000000, 0x01, 0xffff);
		memTable.Add(0x010100000000, 0x02, 0xfff2);
		using (var ptable = ConstructPTable(memTable)) {
			IndexEntry entry;
			Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
			Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
			Assert.AreEqual(0x01, entry.Version);
			Assert.AreEqual(0xffff, entry.Position);
		}
	}

	[Test]
	public void when_duplicated_entries_exist_the_one_with_oldest_position_is_returned() {
		var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memTable.Add(0x010100000000, 0x01, 0xfff1);
		memTable.Add(0x010100000000, 0x02, 0xfff2);
		memTable.Add(0x010100000000, 0x01, 0xfff3);
		memTable.Add(0x010100000000, 0x02, 0xfff4);
		using (var ptable = ConstructPTable(memTable)) {
			IndexEntry entry;
			Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
			Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
			Assert.AreEqual(0x01, entry.Version);
			Assert.AreEqual(0xfff1, entry.Position);
		}
	}

	[Test]
	public void only_entry_with_smallest_position_is_returned_when_triduplicated() {
		var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memTable.Add(0x010100000000, 0x01, 0xfff1);
		memTable.Add(0x010100000000, 0x01, 0xfff3);
		memTable.Add(0x010100000000, 0x01, 0xfff5);
		using (var ptable = ConstructPTable(memTable)) {
			IndexEntry entry;
			Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
			Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
			Assert.AreEqual(0x01, entry.Version);
			Assert.AreEqual(0xfff1, entry.Position);
		}
	}
}
