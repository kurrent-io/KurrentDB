// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Index;
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;

namespace EventStore.Core.Tests.Index.IndexV4;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class when_merging_ptables_vx_to_v4 : SpecificationWithDirectoryPerTestFixture {
	private readonly List<string> _files = new List<string>();
	private readonly List<PTable> _tables = new List<PTable>();

	private PTable _newtable;
	private string _newtableFile;
	private byte _fromVersion;
	private bool _skipIndexVerify;

	public when_merging_ptables_vx_to_v4(byte fromVersion, bool skipIndexVerify) {
		_fromVersion = fromVersion;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_files.Add(GetTempFilePath());
		var table = new HashListMemTable(_fromVersion, maxSize: 20);
		if (_fromVersion == PTableVersions.IndexV1) {
			table.Add(0x010100000000, 0, 0x0101);
			table.Add(0x010200000000, 0, 0x0102);
			table.Add(0x010300000000, 0, 0x0103);
			table.Add(0x010400000000, 0, 0x0104);
		} else {
			table.Add(0x0101, 0, 0x0101);
			table.Add(0x0102, 0, 0x0102);
			table.Add(0x0103, 0, 0x0103);
			table.Add(0x0104, 0, 0x0104);
		}

		_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify));
		table = new HashListMemTable(_fromVersion, maxSize: 20);

		if (_fromVersion == PTableVersions.IndexV1) {
			table.Add(0x010500000000, 0, 0x0105);
			table.Add(0x010600000000, 0, 0x0106);
			table.Add(0x010700000000, 0, 0x0107);
			table.Add(0x010800000000, 0, 0x0108);
		} else {
			table.Add(0x0105, 0, 0x0105);
			table.Add(0x0106, 0, 0x0106);
			table.Add(0x0107, 0, 0x0107);
			table.Add(0x0108, 0, 0x0108);
		}

		_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify));
		_newtableFile = GetTempFilePath();
		_newtable = PTable.MergeTo(_tables, _newtableFile,
			PTableVersions.IndexV4, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
			skipIndexVerify: _skipIndexVerify);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_newtable.Dispose();
		foreach (var ssTable in _tables) {
			ssTable.Dispose();
		}

		return base.TestFixtureTearDown();
	}

	[Test]
	public void merged_ptable_is_64bit() {
		Assert.AreEqual(PTableVersions.IndexV4, _newtable.Version);
	}

	[Test]
	public void there_are_8_records_in_the_merged_index() {
		Assert.AreEqual(8, _newtable.Count);
	}

	[Test]
	public void midpoints_are_cached_in_ptable_footer() {
		var numIndexEntries = 8;
		var requiredMidpoints = PTable.GetRequiredMidpointCountCached(numIndexEntries, PTableVersions.IndexV4);

		var newTableFileCopy = GetTempFilePath();
		File.Copy(_newtableFile, newTableFileCopy);
		using (var filestream = File.Open(newTableFileCopy, FileMode.Open, FileAccess.Read)) {
			var footerSize = PTableFooter.GetSize(PTableVersions.IndexV4);
			Assert.AreEqual(filestream.Length,
				PTableHeader.Size + numIndexEntries * PTable.IndexEntryV4Size +
				requiredMidpoints * PTable.IndexEntryV4Size + footerSize + PTable.MD5Size);
			filestream.Seek(
				PTableHeader.Size + numIndexEntries * PTable.IndexEntryV4Size +
				requiredMidpoints * PTable.IndexEntryV4Size, SeekOrigin.Begin);

			var ptableFooter = PTableFooter.FromStream(filestream);
			Assert.AreEqual(FileType.PTableFile, ptableFooter.FileType);
			Assert.AreEqual(PTableVersions.IndexV4, ptableFooter.Version);
			Assert.AreEqual(requiredMidpoints, ptableFooter.NumMidpointsCached);
		}
	}

	[Test]
	public void correct_number_of_midpoints_are_loaded() {
		Assert.AreEqual(_newtable.GetMidPoints().Length,
			PTable.GetRequiredMidpointCountCached(8, PTableVersions.IndexV4));
	}

	[Test]
	public void all_the_entries_have_upgraded_hashes() {
		if (_fromVersion == PTableVersions.IndexV1) {
			foreach (var item in _newtable.IterateAllInOrder()) {
				Assert.IsTrue((ulong)item.Position == item.Stream - 1,
					"Expected the Stream (Hash) {0} to be equal to {1}", item.Stream - 1, item.Position);
			}
		}
	}

	[Test]
	public void none_of_the_entries_have_upgraded_hashes() {
		if (_fromVersion > PTableVersions.IndexV1) {
			foreach (var item in _newtable.IterateAllInOrder()) {
				Assert.IsTrue((ulong)item.Position == item.Stream,
					"Expected the Stream (Hash) {0} to be equal to {1}", item.Stream, item.Position);
			}
		}
	}
}

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class when_merging_to_ptable_v4_with_deleted_entries : SpecificationWithDirectoryPerTestFixture {
	private readonly List<string> _files = new List<string>();
	private readonly List<PTable> _tables = new List<PTable>();
	private string _newtableFile;

	private PTable _newtable;
	private byte _fromVersion;
	private bool _skipIndexVerify;

	public when_merging_to_ptable_v4_with_deleted_entries(byte fromVersion, bool skipIndexVerify) {
		_fromVersion = fromVersion;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_files.Add(GetTempFilePath());
		var table = new HashListMemTable(_fromVersion, maxSize: 20);
		table.Add(0x010100000000, 0, 1);
		table.Add(0x010200000000, 0, 2);
		table.Add(0x010300000000, 0, 3);
		table.Add(0x010300000000, 1, 4);
		_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify));
		table = new HashListMemTable(_fromVersion, maxSize: 20);
		table.Add(0x010100000000, 2, 5);
		table.Add(0x010200000000, 1, 6);
		table.Add(0x010200000000, 2, 7);
		table.Add(0x010400000000, 0, 8);
		table.Add(0x010400000000, 1, 9);
		_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify));
		table = new HashListMemTable(_fromVersion, maxSize: 20);
		table.Add(0x010100000000, 1, 10);
		table.Add(0x010100000000, 2, 11);
		table.Add(0x010500000000, 1, 12);
		table.Add(0x010500000000, 2, 13);
		table.Add(0x010500000000, 3, 14);
		_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify));
		_newtableFile = GetTempFilePath();
		_newtable = PTable.MergeTo(_tables, _newtableFile,
			PTableVersions.IndexV4, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_newtable.Dispose();
		foreach (var ssTable in _tables) {
			ssTable.Dispose();
		}

		return base.TestFixtureTearDown();
	}

	[Test]
	public void merged_ptable_is_64bit() {
		Assert.AreEqual(PTableVersions.IndexV4, _newtable.Version);
	}

	[Test]
	public void there_are_14_records_in_the_merged_index() {
		Assert.AreEqual(14, _newtable.Count);
	}

	[Test]
	public void midpoints_are_cached_in_ptable_footer() {
		var numIndexEntries = 14;
		var requiredMidpoints = PTable.GetRequiredMidpointCountCached(numIndexEntries, PTableVersions.IndexV4);

		var newTableFileCopy = GetTempFilePath();
		File.Copy(_newtableFile, newTableFileCopy);
		using (var filestream = File.Open(newTableFileCopy, FileMode.Open, FileAccess.Read)) {
			var footerSize = PTableFooter.GetSize(PTableVersions.IndexV4);
			Assert.AreEqual(filestream.Length,
				PTableHeader.Size + numIndexEntries * PTable.IndexEntryV4Size +
				requiredMidpoints * PTable.IndexEntryV4Size + footerSize + PTable.MD5Size);
			filestream.Seek(
				PTableHeader.Size + numIndexEntries * PTable.IndexEntryV4Size +
				requiredMidpoints * PTable.IndexEntryV4Size, SeekOrigin.Begin);

			var ptableFooter = PTableFooter.FromStream(filestream);
			Assert.AreEqual(FileType.PTableFile, ptableFooter.FileType);
			Assert.AreEqual(PTableVersions.IndexV4, ptableFooter.Version);
			Assert.AreEqual(requiredMidpoints, ptableFooter.NumMidpointsCached);
		}
	}

	[Test]
	public void correct_number_of_midpoints_are_loaded() {
		Assert.AreEqual(_newtable.GetMidPoints().Length,
			PTable.GetRequiredMidpointCountCached(14, PTableVersions.IndexV4));
	}

	[Test]
	public void the_items_are_sorted() {
		var last = new IndexEntry(ulong.MaxValue, 0, long.MaxValue);
		foreach (var item in _newtable.IterateAllInOrder()) {
			Assert.IsTrue((last.Stream == item.Stream ? last.Version > item.Version : last.Stream > item.Stream) ||
						  ((last.Stream == item.Stream && last.Version == item.Version) &&
						   last.Position > item.Position));
			last = item;
		}
	}
}
