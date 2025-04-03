// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
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
public class when_scavenging_an_index_removes_nothing : SpecificationWithDirectoryPerTestFixture {
	private PTable _newtable;
	private readonly byte _oldVersion;
	private bool _skipIndexVerify;
	private PTable _oldTable;
	private string _expectedOutputFile;

	public when_scavenging_an_index_removes_nothing(byte oldVersion, bool skipIndexVerify) {
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

		Func<IndexEntry, bool> existsAt = x => true;

		_expectedOutputFile = GetTempFilePath();
		(_newtable, var spaceSaved) = await PTable.Scavenged(_oldTable, _expectedOutputFile,
			PTableVersions.IndexV4, existsAt.ToAsync(), skipIndexVerify: _skipIndexVerify,
			initialReaders: Constants.PTableInitialReaderCount, maxReaders: Constants.PTableMaxReaderCountDefault);
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_oldTable.Dispose();
		_newtable?.Dispose();

		return base.TestFixtureTearDown();
	}

	[Test]
	public void a_null_object_is_returned_if_the_version_is_unchanged() {
		if (_oldVersion == PTableVersions.IndexV4) {
			Assert.IsNull(_newtable);
		}
	}

	[Test]
	public void the_output_file_is_deleted_if_version_is_unchanged() {
		if (_oldVersion == PTableVersions.IndexV4) {
			Assert.That(File.Exists(_expectedOutputFile), Is.False);
		}
	}

	[Test]
	public void a_table_with_all_items_is_returned_with_a_newer_version() {
		if (_oldVersion != PTableVersions.IndexV4) {
			Assert.IsNotNull(_newtable);
			Assert.That(_newtable.Count, Is.EqualTo(4));
		}
	}
}
