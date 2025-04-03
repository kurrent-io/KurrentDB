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
public class when_merging_two_ptables : SpecificationWithDirectoryPerTestFixture {
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private readonly List<string> _files = new List<string>();
	private readonly List<PTable> _tables = new List<PTable>();

	private PTable _newtable;

	private bool _skipIndexVerify;

	public when_merging_two_ptables(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	private ulong GetHash(ulong value) {
		return _ptableVersion == PTableVersions.IndexV1 ? value >> 32 : value;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		for (int i = 0; i < 2; i++) {
			_files.Add(GetTempFilePath());

			var table = new HashListMemTable(_ptableVersion, maxSize: 20);
			for (int j = 0; j < 10; j++) {
				table.Add((ulong)(0x010100000000 << (j + 1)), i + 1, i * j);
			}

			_tables.Add(PTable.FromMemtable(table, _files[i], Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
				skipIndexVerify: _skipIndexVerify, useBloomFilter: true));
		}

		_files.Add(GetTempFilePath());
		_newtable = PTable.MergeTo(_tables, _files[2],
			_ptableVersion, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
			skipIndexVerify: _skipIndexVerify, useBloomFilter: true);
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
	public void there_are_twenty_records_in_merged_index() {
		Assert.AreEqual(20, _newtable.Count);
	}

	[Test]
	public void the_streams_can_be_found() {
		for (int j = 0; j < 10; j++) {
			var stream = (ulong)(0x010100000000 << (j + 1));
			Assert.True(_newtable.TryGetLatestEntry(stream, out var entry));
			Assert.AreEqual(GetHash(stream), entry.Stream);
			Assert.AreEqual(2, entry.Version);
			Assert.AreEqual(1 * j, entry.Position);
		}
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
