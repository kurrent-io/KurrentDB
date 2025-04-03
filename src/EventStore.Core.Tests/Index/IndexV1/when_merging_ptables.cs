// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class when_merging_ptables : SpecificationWithDirectoryPerTestFixture {
	private readonly List<string> _files = new List<string>();
	private readonly List<PTable> _tables = new List<PTable>();

	private PTable _newtable;
	private readonly byte _ptableVersion;
	private readonly bool _skipIndexVerify;

	public when_merging_ptables(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_files.Add(GetTempFilePath());
		var table = new HashListMemTable(_ptableVersion, maxSize: 20);
		table.Add(0x0101, 0, 0x0101);
		table.Add(0x0102, 0, 0x0102);
		table.Add(0x0103, 0, 0x0103);
		table.Add(0x0104, 0, 0x0104);
		_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
		table = new HashListMemTable(_ptableVersion, maxSize: 20);
		table.Add(0x0105, 0, 0x0105);
		table.Add(0x0106, 0, 0x0106);
		table.Add(0x0107, 0, 0x0107);
		table.Add(0x0108, 0, 0x0108);
		_tables.Add(PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault));
		_newtable = PTable.MergeTo(
			tables: _tables,
			outputFile: GetTempFilePath(),
			version: PTableVersions.IndexV4,
			initialReaders: Constants.PTableInitialReaderCount,
			maxReaders: Constants.PTableMaxReaderCountDefault,
			skipIndexVerify: _skipIndexVerify,
			useBloomFilter: true);
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
	public void a_stream_can_be_found() {
		Assert.True(_newtable.TryGetLatestEntry(0x0108, out var entry));
		Assert.AreEqual(0x0108, entry.Stream);
		Assert.AreEqual(0, entry.Version);
		Assert.AreEqual(0x0108, entry.Position);
	}

	[Test]
	public void no_entries_should_have_changed() {
		foreach (var item in _newtable.IterateAllInOrder()) {
			Assert.IsTrue((ulong)item.Position == item.Stream, "Expected the Stream (Hash) {0:X} to be equal to {1:X}",
				item.Stream - 1, item.Position);
		}
	}
}
