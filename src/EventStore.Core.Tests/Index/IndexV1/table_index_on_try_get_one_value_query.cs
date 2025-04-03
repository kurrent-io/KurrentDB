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

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class table_index_on_try_get_one_value_query : SpecificationWithDirectoryPerTestFixture {
	private TableIndex<string> _tableIndex;
	private IHasher<string> _lowHasher;
	private IHasher<string> _highHasher;
	private string _indexDir;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;

	public table_index_on_try_get_one_value_query(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_indexDir = PathName;
		var fakeReader = new TFReaderLease(new FakeTfReader());
		_lowHasher = new XXHashUnsafe();
		_highHasher = new Murmur3AUnsafe();
		_tableIndex = new TableIndex<string>(_indexDir, _lowHasher, _highHasher, "",
			() => new HashListMemTable(_ptableVersion, maxSize: 10),
			() => fakeReader,
			_ptableVersion,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: 5,
			skipIndexVerify: _skipIndexVerify);
		_tableIndex.Initialize(long.MaxValue);

		_tableIndex.Add(0, "0xDEAD", 0, 0xFF00);
		_tableIndex.Add(0, "0xDEAD", 1, 0xFF01);

		_tableIndex.Add(0, "0xBEEF", 0, 0xFF00);
		_tableIndex.Add(0, "0xBEEF", 1, 0xFF01);

		_tableIndex.Add(0, "0xABBA", 0, 0xFF00); // 1st ptable0

		_tableIndex.Add(0, "0xABBA", 1, 0xFF01);
		_tableIndex.Add(0, "0xABBA", 2, 0xFF02);
		_tableIndex.Add(0, "0xABBA", 3, 0xFF03);

		_tableIndex.Add(0, "0xADA", 0,
			0xFF00); // simulates duplicate due to concurrency in TableIndex (see memtable below)
		_tableIndex.Add(0, "0xDEAD", 0, 0xFF10); // 2nd ptable0

		_tableIndex.Add(0, "0xDEAD", 1, 0xFF11); // in memtable
		_tableIndex.Add(0, "0xADA", 0, 0xFF00); // in memtable
	}


	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_tableIndex.Close();

		return base.TestFixtureTearDown();
	}

	[Test]
	public void should_return_empty_collection_when_stream_is_not_in_db() {
		long position;
		Assert.IsFalse(_tableIndex.TryGetOneValue("0xFEED", 0, out position));
	}

	[Test]
	public void should_return_element_with_largest_position_when_hash_collisions() {
		long position;
		Assert.IsTrue(_tableIndex.TryGetOneValue("0xDEAD", 0, out position));
		Assert.AreEqual(0xFF10, position);
	}

	[Test]
	public void should_return_only_one_element_if_concurrency_duplicate_happens_on_range_query_as_well() {
		var res = _tableIndex.GetRange("0xADA", 0, 100).ToList();
		ulong hash = (ulong)_lowHasher.Hash("0xADA");
		hash = _ptableVersion == PTableVersions.IndexV1 ? hash : hash << 32 | _highHasher.Hash("0xADA");
		Assert.That(res.Count(), Is.EqualTo(1));
		Assert.That(res[0].Stream, Is.EqualTo(hash));
		Assert.That(res[0].Version, Is.EqualTo(0));
		Assert.That(res[0].Position, Is.EqualTo(0xFF00));
	}
}
