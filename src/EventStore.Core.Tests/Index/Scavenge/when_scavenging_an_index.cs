// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.Scavenge;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class when_scavenging_an_index : SpecificationWithDirectoryPerTestFixture {
	private PTable _newtable;
	private readonly byte _oldVersion;
	private bool _skipIndexVerify;
	private PTable _oldTable;

	public when_scavenging_an_index(byte oldVersion, bool skipIndexVerify) {
		_oldVersion = oldVersion;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var table = new HashListMemTable(_oldVersion, maxSize: 20);
		table.Add(0x010100000000, 0, 1);
		table.Add(0x010200000000, 0, 2);
		table.Add(0x010300000000, 0, 3);
		table.Add(0x010300000000, 1, 4);
		_oldTable = PTable.FromMemtable(table, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault);

		Func<IndexEntry, bool> existsAt = x => x.Position % 2 == 0;

		(_newtable, _) = await PTable.Scavenged(_oldTable, GetTempFilePath(),
			PTableVersions.IndexV4, existsAt.ToAsync(), skipIndexVerify: _skipIndexVerify,
			initialReaders: Constants.PTableInitialReaderCount, maxReaders: Constants.PTableMaxReaderCountDefault,
			useBloomFilter: true);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_oldTable.Dispose();
		_newtable.Dispose();

		return base.TestFixtureTearDown();
	}

	[Test]
	public void scavenged_ptable_is_newest_version() {
		Assert.AreEqual(PTableVersions.IndexV4, _newtable.Version);
	}

	[Test]
	public void there_are_2_records_in_the_merged_index() {
		Assert.AreEqual(2, _newtable.Count);
	}

	[Test]
	public void a_stream_can_be_found() {
		var stream = (ulong)0x010300000000;
		Assert.True(_newtable.TryGetLatestEntry(stream, out var entry));
		Assert.AreEqual(stream, entry.Stream);
		Assert.AreEqual(1, entry.Version);
		Assert.AreEqual(4, entry.Position);
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
