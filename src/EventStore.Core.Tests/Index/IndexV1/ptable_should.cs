// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
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
public class ptable_should : SpecificationWithFilePerTestFixture {
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private PTable _ptable;
	private bool _skipIndexVerify;

	public ptable_should(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var table = new HashListMemTable(_ptableVersion, maxSize: 10);
		table.Add(0x010100000000, 0x0001, 0x0001);
		_ptable = PTable.FromMemtable(table, Filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, cacheDepth: 0, skipIndexVerify: _skipIndexVerify);
	}

	public override void TestFixtureTearDown() {
		_ptable.Dispose();
		base.TestFixtureTearDown();
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_range_query_when_provided_with_negative_start_version() {
		Assert.Throws<ArgumentOutOfRangeException>(() => _ptable.GetRange(0x0000, -1, long.MaxValue).ToArray());
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_range_query_when_provided_with_negative_end_version() {
		Assert.Throws<ArgumentOutOfRangeException>(() => _ptable.GetRange(0x0000, 0, -1).ToArray());
	}

	[Test]
	public void throw_argumentoutofrangeexception_on_get_one_entry_query_when_provided_with_negative_version() {
		long pos;
		Assert.Throws<ArgumentOutOfRangeException>(() => _ptable.TryGetOneValue(0x0000, -1, out pos));
	}
}
