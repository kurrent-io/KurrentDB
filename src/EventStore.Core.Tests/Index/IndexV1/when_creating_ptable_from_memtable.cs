// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class when_creating_ptable_from_memtable : SpecificationWithFile {
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;

	public when_creating_ptable_from_memtable(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[Test]
	public void null_file_throws_null_exception() {
		Assert.Throws<ArgumentNullException>(() =>
			PTable.FromMemtable(new HashListMemTable(_ptableVersion, maxSize: 10), null, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
				skipIndexVerify: _skipIndexVerify));
	}

	[Test]
	public void null_memtable_throws_null_exception() {
		Assert.Throws<ArgumentNullException>(() =>
			PTable.FromMemtable(null, "C:\\foo.txt", Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify));
	}

	[Test]
	public void wait_for_destroy_will_timeout() {
		var table = new HashListMemTable(_ptableVersion, maxSize: 10);
		table.Add(0x010100000000, 0x0001, 0x0001);
		var ptable = PTable.FromMemtable(table, Filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify);
		Assert.Throws<TimeoutException>(() => ptable.WaitForDisposal(1));

		// tear down
		ptable.MarkForDestruction();
		ptable.WaitForDisposal(1000);
	}

	//[Test]
	//public void non_power_of_two_throws_invalid_operation()
	//{
	//    var table = new HashListMemTable(_ptableVersion, );
	//    table.Add(0x010100000000, 0x0001, 0x0001);
	//    table.Add(0x010500000000, 0x0001, 0x0002);
	//    table.Add(0x010200000000, 0x0001, 0x0003);
	//    Assert.Throws<InvalidOperationException>(() => PTable.FromMemtable(table, "C:\\foo.txt"));
	//}

	[Test]
	public void the_file_gets_created_and_can_be_read() {
		var indexEntrySize = PTable.IndexEntryV4Size;
		if (_ptableVersion == PTableVersions.IndexV1) {
			indexEntrySize = PTable.IndexEntryV1Size;
		} else if (_ptableVersion == PTableVersions.IndexV2) {
			indexEntrySize = PTable.IndexEntryV2Size;
		} else if (_ptableVersion == PTableVersions.IndexV3) {
			indexEntrySize = PTable.IndexEntryV3Size;
		}

		var table = new HashListMemTable(_ptableVersion, maxSize: 10);
		table.Add(0x010100000000, 0x0001, 0x0001);
		table.Add(0x010500000000, 0x0001, 0x0002);
		table.Add(0x010200000000, 0x0001, 0x0003);
		table.Add(0x010200000000, 0x0002, 0x0003);
		using (var sstable = PTable.FromMemtable(table, Filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
			skipIndexVerify: _skipIndexVerify,
			useBloomFilter: true)) {

			var fileinfo = new FileInfo(Filename);
			var midpointsCached = PTable.GetRequiredMidpointCountCached(4, _ptableVersion);
			Assert.AreEqual(
				PTableHeader.Size + 4 * indexEntrySize + midpointsCached * indexEntrySize +
				PTableFooter.GetSize(_ptableVersion) + PTable.MD5Size, fileinfo.Length);
			var items = sstable.IterateAllInOrder().ToList();
			Assert.AreEqual(GetHash(0x010500000000), items[0].Stream);
			Assert.AreEqual(0x0001, items[0].Version);
			Assert.AreEqual(GetHash(0x010200000000), items[1].Stream);
			Assert.AreEqual(0x0002, items[1].Version);
			Assert.AreEqual(GetHash(0x010200000000), items[2].Stream);
			Assert.AreEqual(0x0001, items[2].Version);
			Assert.AreEqual(GetHash(0x010100000000), items[3].Stream);
			Assert.AreEqual(0x0001, items[3].Version);

			var stream = (ulong)0x010200000000;
			Assert.True(sstable.TryGetLatestEntry(stream, out var entry));
			Assert.AreEqual(GetHash(stream), entry.Stream);
			Assert.AreEqual(0x0002, entry.Version);
			Assert.AreEqual(0x0003, entry.Position);
		}
	}

	[Test]
	public void the_hash_of_file_is_valid() {
		var table = new HashListMemTable(_ptableVersion, maxSize: 10);
		table.Add(0x010100000000, 0x0001, 0x0001);
		table.Add(0x010500000000, 0x0001, 0x0002);
		table.Add(0x010200000000, 0x0001, 0x0003);
		table.Add(0x010200000000, 0x0002, 0x0003);
		Assert.DoesNotThrow(() => {
			using (var sstable = PTable.FromMemtable(table, Filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: false)) {
			}
		});
	}

	private ulong GetHash(ulong value) {
		return _ptableVersion == PTableVersions.IndexV1 ? value >> 32 : value;
	}
}
