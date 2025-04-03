// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading.Tasks;
using EventStore.Common.Options;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2)]
[TestFixture(PTableVersions.IndexV3)]
[TestFixture(PTableVersions.IndexV4)]
public class when_a_ptable_is_corrupt_on_disk : SpecificationWithDirectory {
	private string _filename;
	private PTable _table;
	private string _copiedfilename;
	protected byte _ptableVersion = PTableVersions.IndexV1;

	public when_a_ptable_is_corrupt_on_disk(byte version) {
		_ptableVersion = version;
	}

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();

		_filename = GetTempFilePath();
		_copiedfilename = GetTempFilePath();
		var mtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		mtable.Add(0x010100000000, 0x0001, 0x0001);
		mtable.Add(0x010500000000, 0x0001, 0x0002);
		_table = PTable.FromMemtable(mtable, _filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault);
		_table.Dispose();
		File.Copy(_filename, _copiedfilename);
		using (var f = new FileStream(_copiedfilename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
			f.Seek(130, SeekOrigin.Begin);
			f.WriteByte(0x22);
		}
	}

	[TearDown]
	public override Task TearDown() {
		_table.MarkForDestruction();
		_table.WaitForDisposal(1000);

		return base.TearDown();
	}

	[Test]
	public void the_hash_is_invalid() {
		var exc = Assert.Throws<CorruptIndexException>(() => PTable.FromFile(_copiedfilename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, 16, false));
		Assert.IsInstanceOf<HashValidationException>(exc.InnerException);
	}
}
