// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.TransactionLog;
using NUnit.Framework;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false), Category("LongRunning")]
[TestFixture(PTableVersions.IndexV2, true), Category("LongRunning")]
[TestFixture(PTableVersions.IndexV3, false), Category("LongRunning")]
[TestFixture(PTableVersions.IndexV3, true), Category("LongRunning")]
[TestFixture(PTableVersions.IndexV4, false), Category("LongRunning")]
[TestFixture(PTableVersions.IndexV4, true), Category("LongRunning")]
public class table_index_with_two_ptables_and_memtable_on_range_query : SpecificationWithDirectoryPerTestFixture {
	private TableIndex<string> _tableIndex;
	private IHasher<string> _lowHasher;
	private IHasher<string> _highHasher;
	private string _indexDir;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;

	public table_index_with_two_ptables_and_memtable_on_range_query(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_indexDir = PathName;
		var fakeReader = new TFReaderLease(new FakeIndexReader());
		_lowHasher = new FakeIndexHasher();
		_highHasher = new FakeIndexHasher();
		_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, "",
			() => new HashListMemTable(_ptableVersion, maxSize: 10),
			() => fakeReader,
			_ptableVersion,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: 2,
			maxTablesPerLevel: 2,
			skipIndexVerify: _skipIndexVerify);
		_tableIndex.Initialize(long.MaxValue);

		// ptable level 2
		_tableIndex.Add(0, "1", 0, 0xFF00);
		_tableIndex.Add(0, "1", 1, 0xFF01);
		_tableIndex.Add(0, "2", 0, 0xFF00);
		_tableIndex.Add(0, "2", 1, 0xFF01);
		_tableIndex.Add(0, "3", 0, 0xFF00);
		_tableIndex.Add(0, "3", 1, 0xFF01);
		_tableIndex.Add(0, "3", 0, 0xFF02);
		_tableIndex.Add(0, "3", 1, 0xFF03);

		// ptable level 1
		_tableIndex.Add(0, "4", 0, 0xFF00);
		_tableIndex.Add(0, "5", 10, 0xFFF1);
		_tableIndex.Add(0, "6", 0, 0xFF00);
		_tableIndex.Add(0, "1", 0, 0xFF10);

		// ptable level 0
		_tableIndex.Add(0, "6", 1, 0xFF01);
		_tableIndex.Add(0, "1", 1, 0xFF11);

		// memtable
		_tableIndex.Add(0, "4", 0, 0xFF01);

		await Task.Delay(500);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_tableIndex.Close();

		return base.TestFixtureTearDown();
	}

	private ulong GetHash(string streamId) {
		ulong hash = _lowHasher.Hash(streamId);
		hash = _ptableVersion == PTableVersions.IndexV1 ? hash : hash << 32 | _highHasher.Hash(streamId);
		return hash;
	}

	[Test]
	public void should_not_return_latest_entry_for_nonexisting_stream() {
		IndexEntry entry;
		Assert.IsFalse(_tableIndex.TryGetLatestEntry("7", out entry));
	}

	[Test]
	public void should_not_return_oldest_entry_for_nonexisting_stream() {
		IndexEntry entry;
		Assert.IsFalse(_tableIndex.TryGetLatestEntry("7", out entry));
	}

	[Test]
	public void should_return_correct_latest_entry_for_stream_with_latest_entry_in_memtable() {
		IndexEntry entry;
		Assert.IsTrue(_tableIndex.TryGetLatestEntry("4", out entry));
		Assert.AreEqual(GetHash("4"), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0xFF01, entry.Position);
	}

	[Test]
	public void should_return_correct_latest_entry_for_stream_with_latest_entry_in_ptable_0() {
		IndexEntry entry;
		Assert.IsTrue(_tableIndex.TryGetLatestEntry("1", out entry));
		Assert.AreEqual(GetHash("1"), entry.Stream);
		Assert.AreEqual(1, entry.Version);
		Assert.AreEqual(0xFF11, entry.Position);
	}

	[Test]
	public void should_return_correct_latest_entry_for_another_stream_with_latest_entry_in_ptable_0() {
		IndexEntry entry;
		Assert.IsTrue(_tableIndex.TryGetLatestEntry("6", out entry));
		Assert.AreEqual(GetHash("6"), entry.Stream);
		Assert.AreEqual(1, entry.Version);
		Assert.AreEqual(0xFF01, entry.Position);
	}

	[Test]
	public void should_return_correct_latest_entry_for_stream_with_latest_entry_in_ptable_1() {
		IndexEntry entry;
		Assert.IsTrue(_tableIndex.TryGetLatestEntry("5", out entry));
		Assert.AreEqual(GetHash("5"), entry.Stream);
		Assert.AreEqual(10, entry.Version);
		Assert.AreEqual(0xFFF1, entry.Position);
	}

	[Test]
	public void should_return_correct_latest_entry_for_stream_with_latest_entry_in_ptable_2() {
		IndexEntry entry;
		Assert.IsTrue(_tableIndex.TryGetLatestEntry("2", out entry));
		Assert.AreEqual(GetHash("2"), entry.Stream);
		Assert.AreEqual(1, entry.Version);
		Assert.AreEqual(0xFF01, entry.Position);
	}

	[Test]
	public void should_return_correct_latest_entry_for_another_stream_with_latest_entry_in_ptable_2() {
		IndexEntry entry;
		Assert.IsTrue(_tableIndex.TryGetLatestEntry("3", out entry));
		Assert.AreEqual(GetHash("3"), entry.Stream);
		Assert.AreEqual(1, entry.Version);
		Assert.AreEqual(0xFF03, entry.Position);
	}

	[Test]
	public void should_return_correct_oldest_entries_for_each_stream() {
		IndexEntry entry;

		Assert.IsTrue(_tableIndex.TryGetOldestEntry("1", out entry));
		Assert.AreEqual(GetHash("1"), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0xFF00, entry.Position);

		Assert.IsTrue(_tableIndex.TryGetOldestEntry("2", out entry));
		Assert.AreEqual(GetHash("2"), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0xFF00, entry.Position);

		Assert.IsTrue(_tableIndex.TryGetOldestEntry("3", out entry));
		Assert.AreEqual(GetHash("3"), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0xFF00, entry.Position);

		Assert.IsTrue(_tableIndex.TryGetOldestEntry("4", out entry));
		Assert.AreEqual(GetHash("4"), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0xFF00, entry.Position);

		Assert.IsTrue(_tableIndex.TryGetOldestEntry("5", out entry));
		Assert.AreEqual(GetHash("5"), entry.Stream);
		Assert.AreEqual(10, entry.Version);
		Assert.AreEqual(0xFFF1, entry.Position);

		Assert.IsTrue(_tableIndex.TryGetOldestEntry("6", out entry));
		Assert.AreEqual(GetHash("6"), entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0xFF00, entry.Position);
	}

	[Test]
	public void should_return_empty_range_for_nonexisting_stream() {
		var range = _tableIndex.GetRange("7", 0, int.MaxValue).ToArray();
		Assert.AreEqual(0, range.Length);
	}

	[Test]
	public void should_return_correct_full_range_with_descending_order_for_1() {
		var range = _tableIndex.GetRange("1", 0, long.MaxValue).ToArray();
		Assert.AreEqual(4, range.Length);
		Assert.AreEqual(new IndexEntry(GetHash("1"), 1, 0xFF11), range[0]);
		Assert.AreEqual(new IndexEntry(GetHash("1"), 1, 0xFF01), range[1]);
		Assert.AreEqual(new IndexEntry(GetHash("1"), 0, 0xFF10), range[2]);
		Assert.AreEqual(new IndexEntry(GetHash("1"), 0, 0xFF00), range[3]);
	}

	[Test]
	public void should_return_correct_full_range_with_descending_order_for_2() {
		var range = _tableIndex.GetRange("2", 0, long.MaxValue).ToArray();
		Assert.AreEqual(2, range.Length);
		Assert.AreEqual(new IndexEntry(GetHash("2"), 1, 0xFF01), range[0]);
		Assert.AreEqual(new IndexEntry(GetHash("2"), 0, 0xFF00), range[1]);
	}

	[Test]
	public void should_return_correct_full_range_with_descending_order_for_3() {
		var range = _tableIndex.GetRange("3", 0, long.MaxValue).ToArray();
		Assert.AreEqual(4, range.Length);
		Assert.AreEqual(new IndexEntry(GetHash("3"), 1, 0xFF03), range[0]);
		Assert.AreEqual(new IndexEntry(GetHash("3"), 1, 0xFF01), range[1]);
		Assert.AreEqual(new IndexEntry(GetHash("3"), 0, 0xFF02), range[2]);
		Assert.AreEqual(new IndexEntry(GetHash("3"), 0, 0xFF00), range[3]);
	}

	[Test]
	public void should_return_correct_full_range_with_descending_order_for_4() {
		var range = _tableIndex.GetRange("4", 0, long.MaxValue).ToArray();
		Assert.AreEqual(2, range.Length);
		Assert.AreEqual(new IndexEntry(GetHash("4"), 0, 0xFF01), range[0]);
		Assert.AreEqual(new IndexEntry(GetHash("4"), 0, 0xFF00), range[1]);
	}

	[Test]
	public void should_return_correct_full_range_with_descending_order_for_5() {
		var range = _tableIndex.GetRange("5", 0, long.MaxValue).ToArray();
		Assert.AreEqual(1, range.Length);
		Assert.AreEqual(new IndexEntry(GetHash("5"), 10, 0xFFF1), range[0]);
	}

	[Test]
	public void should_return_correct_full_range_with_descending_order_for_6() {
		var range = _tableIndex.GetRange("6", 0, long.MaxValue).ToArray();
		Assert.AreEqual(2, range.Length);
		Assert.AreEqual(new IndexEntry(GetHash("6"), 1, 0xFF01), range[0]);
		Assert.AreEqual(new IndexEntry(GetHash("6"), 0, 0xFF00), range[1]);
	}

	[Test]
	public void should_not_return_one_value_for_nonexistent_stream() {
		long pos;
		Assert.IsFalse(_tableIndex.TryGetOneValue("7", 0, out pos));
	}

	[Test]
	public void should_return_one_value_for_existing_streams_for_existing_version() {
		long pos;
		Assert.IsTrue(_tableIndex.TryGetOneValue("1", 1, out pos));
		Assert.AreEqual(0xFF11, pos);

		Assert.IsTrue(_tableIndex.TryGetOneValue("2", 0, out pos));
		Assert.AreEqual(0xFF00, pos);

		Assert.IsTrue(_tableIndex.TryGetOneValue("3", 0, out pos));
		Assert.AreEqual(0xFF02, pos);

		Assert.IsTrue(_tableIndex.TryGetOneValue("4", 0, out pos));
		Assert.AreEqual(0xFF01, pos);

		Assert.IsTrue(_tableIndex.TryGetOneValue("5", 10, out pos));
		Assert.AreEqual(0xFFF1, pos);

		Assert.IsTrue(_tableIndex.TryGetOneValue("6", 1, out pos));
		Assert.AreEqual(0xFF01, pos);
	}

	[Test]
	public void should_not_return_one_value_for_existing_streams_for_nonexistent_version() {
		long pos;
		Assert.IsFalse(_tableIndex.TryGetOneValue("1", 2, out pos));
		Assert.IsFalse(_tableIndex.TryGetOneValue("2", 2, out pos));
		Assert.IsFalse(_tableIndex.TryGetOneValue("3", 2, out pos));
		Assert.IsFalse(_tableIndex.TryGetOneValue("4", 1, out pos));
		Assert.IsFalse(_tableIndex.TryGetOneValue("5", 0, out pos));
		Assert.IsFalse(_tableIndex.TryGetOneValue("6", 2, out pos));
	}
}
