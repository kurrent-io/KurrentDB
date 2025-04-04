// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
public class
	when_merging_ptables_with_entries_to_nonexisting_record_in_newer_index_versions :
		SpecificationWithDirectoryPerTestFixture {
	private readonly List<string> _files = new List<string>();
	private readonly List<PTable> _tables = new List<PTable>();
	protected PTable _newtable;
	protected byte _ptableVersion = PTableVersions.IndexV1;

	private bool _skipIndexVerify;

	public when_merging_ptables_with_entries_to_nonexisting_record_in_newer_index_versions(byte version,
		bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		for (int i = 0; i < 4; i++) {
			_files.Add(GetTempFilePath());

			var table = new HashListMemTable(_ptableVersion, maxSize: 30);
			for (int j = 0; j < 10; j++) {
				table.Add((ulong)(0x010100000000 << i), j, i * 10 + j);
			}

			_tables.Add(PTable.FromMemtable(table, _files[i], Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify));
		}

		_files.Add(GetTempFilePath());
		_newtable = PTable.MergeTo(_tables, _files[4],
			_ptableVersion, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
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
	public void all_entries_are_left() {
		Assert.AreEqual(40, _newtable.Count);
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

	[Test]
	public void the_right_items_are_deleted() {
		for (int i = 0; i < 4; i++) {
			for (int j = 0; j < 10; j++) {
				long position;
				Assert.IsTrue(_newtable.TryGetOneValue((ulong)(0x010100000000 << i), j, out position));
				Assert.AreEqual(i * 10 + j, position);
			}
		}
	}
}
