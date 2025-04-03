// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.Scavenge;

[TestFixture(false)]
[TestFixture(true)]
class
	when_scavenging_a_table_index_and_another_table_is_completed_during : SpecificationWithDirectoryPerTestFixture {
	private TableIndex<string> _tableIndex;
	private IHasher<string> _lowHasher;
	private IHasher<string> _highHasher;
	private string _indexDir;
	private FakeTFScavengerLog _log;
	private readonly bool _useBloomFilter;

	public when_scavenging_a_table_index_and_another_table_is_completed_during(bool useBloomFilter) {
		_useBloomFilter = useBloomFilter;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_indexDir = PathName;

		var scavengeBlocker = new ManualResetEventSlim(false);
		var scavengeStarted = new ManualResetEventSlim(false);

		var fakeReader = new TFReaderLease(new FakeIndexReader(l => {
			scavengeStarted.Set();
			if (!scavengeBlocker.Wait(5000))
				throw new Exception("Failed to continue.");
			return false;
		}));

		_lowHasher = new XXHashUnsafe();
		_highHasher = new Murmur3AUnsafe();
		_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, "",
			() => new HashListMemTable(PTableVersions.IndexV4, maxSize: 5),
			() => fakeReader,
			PTableVersions.IndexV4,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: 2,
			maxTablesPerLevel: 5,
			useBloomFilter: _useBloomFilter);
		_tableIndex.Initialize(long.MaxValue);


		_tableIndex.Add(1, "testStream-1", 0, 0);
		_tableIndex.Add(1, "testStream-1", 1, 100);
		_tableIndex.WaitForBackgroundTasks();

		_log = new FakeTFScavengerLog();
		var task = Task.Run(() => _tableIndex.Scavenge(_log, CancellationToken.None));

		Assert.That(scavengeStarted.Wait(5000));

		// Add enough for 2 more tables
		_tableIndex.Add(1, "testStream-1", 2, 200);
		_tableIndex.Add(1, "testStream-1", 3, 300);
		_tableIndex.Add(1, "testStream-1", 4, 400);
		_tableIndex.Add(1, "testStream-1", 5, 500);

		// Release the scavenge process
		scavengeBlocker.Set();
		await task;

		// Check it's loadable.
		_tableIndex.Close(false);

		_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, "",
			() => new HashListMemTable(PTableVersions.IndexV4, maxSize: 5),
			() => fakeReader,
			PTableVersions.IndexV4,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: 2,
			maxTablesPerLevel: 5,
			useBloomFilter: _useBloomFilter);

		_tableIndex.Initialize(long.MaxValue);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_tableIndex.Close();

		return base.TestFixtureTearDown();
	}

	[Test]
	public void should_have_logged_each_index_table() {
		Assert.That(_log.ScavengedIndices.Count, Is.EqualTo(1));
		Assert.That(_log.ScavengedIndices[0].Scavenged, Is.True);
		Assert.That(_log.ScavengedIndices[0].Error, Is.Null);
		Assert.That(_log.ScavengedIndices[0].EntriesDeleted, Is.EqualTo(2));
	}

	[Test]
	public void should_still_have_all_entries_in_sorted_order() {
		var streamId = "testStream-1";
		var result = _tableIndex.GetRange(streamId, 0, 5).ToArray();
		var hash = (ulong)_lowHasher.Hash(streamId) << 32 | _highHasher.Hash(streamId);

		Assert.That(result.Count(), Is.EqualTo(4));

		Assert.That(result[0].Stream, Is.EqualTo(hash));
		Assert.That(result[0].Version, Is.EqualTo(5));
		Assert.That(result[0].Position, Is.EqualTo(500));

		Assert.That(result[1].Stream, Is.EqualTo(hash));
		Assert.That(result[1].Version, Is.EqualTo(4));
		Assert.That(result[1].Position, Is.EqualTo(400));

		Assert.That(result[2].Stream, Is.EqualTo(hash));
		Assert.That(result[2].Version, Is.EqualTo(3));
		Assert.That(result[2].Position, Is.EqualTo(300));

		Assert.That(result[3].Stream, Is.EqualTo(hash));
		Assert.That(result[3].Version, Is.EqualTo(2));
		Assert.That(result[3].Position, Is.EqualTo(200));
	}


	[Test]
	public void all_tables_are_written_to_disk() {
		if (_useBloomFilter) {
			Assert.That(Directory.EnumerateFiles(_indexDir).Count(), Is.EqualTo(7), "Expected IndexMap and 3 tables and 3 bloom filters.");
		} else {
			Assert.That(Directory.EnumerateFiles(_indexDir).Count(), Is.EqualTo(4), "Expected IndexMap and 3 tables.");
		}
	}
}
