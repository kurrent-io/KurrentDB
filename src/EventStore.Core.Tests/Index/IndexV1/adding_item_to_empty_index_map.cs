// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
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
public class adding_item_to_empty_index_map : SpecificationWithDirectoryPerTestFixture {
	private string _filename;
	private IndexMap _map;
	private string _tablename;
	private string _mergeFile;
	private MergeResult _result;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;
	private int _maxAutoMergeIndexLevel = 4;

	public adding_item_to_empty_index_map(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_filename = GetTempFilePath();
		_tablename = GetTempFilePath();
		_mergeFile = GetFilePathFor("mergefile");

		_map = IndexMapTestFactory.FromFile(_filename, maxAutoMergeLevel: _maxAutoMergeIndexLevel);
		var memtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memtable.Add(0, 1, 0);
		var table = PTable.FromMemtable(memtable, _tablename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify);
		_result = _map.AddAndMergePTable(table, 7, 11,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		table.MarkForDestruction();
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		File.Delete(_filename);
		File.Delete(_mergeFile);
		File.Delete(_tablename);

		return base.TestFixtureTearDown();
	}

	[Test]
	public void the_prepare_checkpoint_is_taken_from_the_latest_added_table() {
		Assert.AreEqual(7, _result.MergedMap.PrepareCheckpoint);
	}

	[Test]
	public void the_commit_checkpoint_is_taken_from_the_latest_added_table() {
		Assert.AreEqual(11, _result.MergedMap.CommitCheckpoint);
	}

	[Test]
	public void there_are_no_items_to_delete() {
		Assert.AreEqual(0, _result.ToDelete.Count);
	}

	[Test]
	public void the_merged_map_has_a_single_file() {
		Assert.AreEqual(1, _result.MergedMap.GetAllFilenames().Count());
		Assert.AreEqual(_tablename, _result.MergedMap.GetAllFilenames().ToList()[0]);
	}

	[Test]
	public void the_original_map_did_not_change() {
		Assert.AreEqual(0, _map.InOrder().Count());
		Assert.AreEqual(0, _map.GetAllFilenames().Count());
	}

	[Test]
	public void a_merged_file_was_not_created() {
		Assert.IsFalse(File.Exists(_mergeFile));
	}
}
