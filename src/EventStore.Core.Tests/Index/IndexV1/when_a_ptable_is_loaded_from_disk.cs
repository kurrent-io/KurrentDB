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

[TestFixture(PTableVersions.IndexV2, false)]
[TestFixture(PTableVersions.IndexV2, true)]
[TestFixture(PTableVersions.IndexV3, false)]
[TestFixture(PTableVersions.IndexV3, true)]
[TestFixture(PTableVersions.IndexV4, false)]
[TestFixture(PTableVersions.IndexV4, true)]
public class when_a_ptable_is_loaded_from_disk : SpecificationWithDirectory {
	private string _filename;
	private PTable _table;
	private string _copiedfilename;
	protected byte _ptableVersion = PTableVersions.IndexV1;

	private bool _skipIndexVerify;

	public when_a_ptable_is_loaded_from_disk(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();

		_filename = GetTempFilePath();
		_copiedfilename = GetTempFilePath();
		var mtable = new HashListMemTable(_ptableVersion, maxSize: 1024);

		long logPosition = 0;
		long eventNumber = 1;
		ulong streamId = 0x010100000000;

		for (var i = 0; i < 1337; i++) {
			logPosition++;

			if (i % 37 == 0) {
				streamId -= 0x1337;
				eventNumber = 1;
			} else
				eventNumber++;

			mtable.Add(streamId, eventNumber, logPosition);
		}

		_table = PTable.FromMemtable(mtable, _filename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify);
		_table.Dispose();
		File.Copy(_filename, _copiedfilename);
	}

	[Test]
	public void same_midpoints_are_loaded_when_enabling_or_disabling_index_verification() {
		for (int depth = 2; depth <= 20; depth++) {
			var ptableWithMD5Verification = PTable.FromFile(_copiedfilename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, false);
			var ptableWithoutVerification = PTable.FromFile(_copiedfilename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, depth, true);
			var midPoints1 = ptableWithMD5Verification.GetMidPoints();
			var midPoints2 = ptableWithoutVerification.GetMidPoints();

			Assert.AreEqual(midPoints1.Length, midPoints2.Length);
			for (var i = 0; i < midPoints1.Length; i++) {
				Assert.AreEqual(midPoints1[i].ItemIndex, midPoints2[i].ItemIndex);
				Assert.AreEqual(midPoints1[i].Key.Stream, midPoints2[i].Key.Stream);
				Assert.AreEqual(midPoints1[i].Key.Version, midPoints2[i].Key.Version);
			}

			ptableWithMD5Verification.Dispose();
			ptableWithoutVerification.Dispose();
		}
	}
}
