// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Index;
using NUnit.Framework;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class table_index_on_range_query : SpecificationWithDirectoryPerTestFixture {
	private TableIndex<string> _tableIndex;
	private IHasher<string> _lowHasher;
	private IHasher<string> _highHasher;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;

	public table_index_on_range_query(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_lowHasher = new XXHashUnsafe();
		_highHasher = new Murmur3AUnsafe();
		_tableIndex = new TableIndex<string>(PathName, _lowHasher, _highHasher, "",
			() => new HashListMemTable(version: _ptableVersion, maxSize: 40),
			() => { throw new InvalidOperationException(); },
			_ptableVersion,
			5, Constants.PTableMaxReaderCountDefault,
			maxSizeForMemory: 5,
			skipIndexVerify: _skipIndexVerify);
		_tableIndex.Initialize(long.MaxValue);

		_tableIndex.Add(0, "0xDEAD", 0, 0xFF00);
		_tableIndex.Add(0, "0xDEAD", 1, 0xFF01);

		_tableIndex.Add(0, "0xJEEP", 0, 0xFF00);
		_tableIndex.Add(0, "0xJEEP", 1, 0xFF01);

		_tableIndex.Add(0, "0xABBA", 0, 0xFF00);
		_tableIndex.Add(0, "0xABBA", 1, 0xFF01);
		_tableIndex.Add(0, "0xABBA", 2, 0xFF02);
		_tableIndex.Add(0, "0xABBA", 3, 0xFF03);

		_tableIndex.Add(0, "0xDEAD", 0, 0xFF10);
		_tableIndex.Add(0, "0xDEAD", 1, 0xFF11);

		_tableIndex.Add(0, "0xADA", 0, 0xFF00);

		_tableIndex.Add(0, "0xJEEP", 2, 0xFFF0);
		_tableIndex.Add(0, "0xJEEP", 3, 0xFFF1);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_tableIndex.Close();
		return base.TestFixtureTearDown();
	}

	[Test]
	public void should_return_empty_collection_when_stream_is_not_in_db() {
		var res = _tableIndex.GetRange("0xFEED", 0, 100);
		Assert.That(res, Is.Empty);
	}

	[Test]
	public void should_return_all_applicable_elements_in_correct_order() {
		var res = _tableIndex.GetRange("0xJEEP", 0, 100).ToList();
		ulong hash = (ulong)_lowHasher.Hash("0xJEEP");
		hash = _ptableVersion == PTableVersions.IndexV1 ? hash : hash << 32 | _highHasher.Hash("0xJEEP");
		Assert.That(res.Count(), Is.EqualTo(4));

		Assert.That(res[0].Stream, Is.EqualTo(hash));
		Assert.That(res[0].Version, Is.EqualTo(3));
		Assert.That(res[0].Position, Is.EqualTo(0xFFF1));

		Assert.That(res[1].Stream, Is.EqualTo(hash));
		Assert.That(res[1].Version, Is.EqualTo(2));
		Assert.That(res[1].Position, Is.EqualTo(0xFFF0));

		Assert.That(res[2].Stream, Is.EqualTo(hash));
		Assert.That(res[2].Version, Is.EqualTo(1));
		Assert.That(res[2].Position, Is.EqualTo(0xFF01));

		Assert.That(res[3].Stream, Is.EqualTo(hash));
		Assert.That(res[3].Version, Is.EqualTo(0));
		Assert.That(res[3].Position, Is.EqualTo(0xFF00));
	}

	[Test]
	public void should_return_all_elements_with_hash_collisions_in_correct_order() {
		var res = _tableIndex.GetRange("0xDEAD", 0, 100).ToList();
		ulong hash = (ulong)_lowHasher.Hash("0xDEAD");
		hash = _ptableVersion == PTableVersions.IndexV1 ? hash : hash << 32 | _highHasher.Hash("0xDEAD");
		Assert.That(res.Count(), Is.EqualTo(4));
		Assert.That(res[0].Stream, Is.EqualTo(hash));
		Assert.That(res[0].Version, Is.EqualTo(1));
		Assert.That(res[0].Position, Is.EqualTo(0xFF11));

		Assert.That(res[1].Stream, Is.EqualTo(hash));
		Assert.That(res[1].Version, Is.EqualTo(1));
		Assert.That(res[1].Position, Is.EqualTo(0xFF01));

		Assert.That(res[2].Stream, Is.EqualTo(hash));
		Assert.That(res[2].Version, Is.EqualTo(0));
		Assert.That(res[2].Position, Is.EqualTo(0xFF10));

		Assert.That(res[3].Stream, Is.EqualTo(hash));
		Assert.That(res[3].Version, Is.EqualTo(0));
		Assert.That(res[3].Position, Is.EqualTo(0xFF00));
	}
}
