// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class destroying_ptable : SpecificationWithFile {
	private PTable _table;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;

	public destroying_ptable(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[SetUp]
	public void Setup() {
		var mtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		mtable.Add(0x010100000000, 0x0001, 0x0001);
		mtable.Add(0x010500000000, 0x0001, 0x0002);
		_table = PTable.FromMemtable(mtable, Filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault,
			skipIndexVerify: _skipIndexVerify,
			useBloomFilter: true);
		_table.MarkForDestruction();
	}

	[Test]
	public void the_file_is_deleted() {
		_table.WaitForDisposal(1000);
		Assert.IsFalse(File.Exists(Filename));
		Assert.IsFalse(File.Exists(_table.BloomFilterFilename));
	}

	[Test]
	public void wait_for_destruction_returns() {
		Assert.DoesNotThrow(() => _table.WaitForDisposal(1000));
	}

	[TearDown]
	public void Teardown() {
		_table.WaitForDisposal(1000);
		File.Delete(Filename);
	}
}
