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
public class
	adding_sixteen_items_to_empty_index_map_with_four_tables_per_level_causes_double_merge :
		SpecificationWithDirectoryPerTestFixture {
	private string _filename;
	private IndexMap _map;
	private string _finalmergefile;
	private string _finalmergefile2;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private int _maxAutoMergeIndexLevel = 4;

	private MergeResult _result;
	private bool _skipIndexVerify;

	public adding_sixteen_items_to_empty_index_map_with_four_tables_per_level_causes_double_merge(byte version,
		bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_filename = GetTempFilePath();
		_finalmergefile = GetTempFilePath();
		_finalmergefile2 = GetTempFilePath();

		_map = IndexMapTestFactory.FromFile(_filename, maxAutoMergeLevel: _maxAutoMergeIndexLevel);
		var memtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memtable.Add(0, 1, 0);
		var guidFilename = new GuidFilenameProvider(PathName);
		_result = _map.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 0, 0,
			guidFilename,
			_ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result = _result.MergedMap.AddAndMergePTable(
			PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify), 1, 2,
			new FakeFilenameProvider(_finalmergefile, _finalmergefile2), _ptableVersion, 0,
			skipIndexVerify: _skipIndexVerify);
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_result.MergedMap.InOrder().ToList().ForEach(x => x.MarkForDestruction());
		File.Delete(_filename);

		return base.TestFixtureTearDown();
	}

	[Test]
	public void the_prepare_checkpoint_is_taken_from_the_latest_added_table() {
		Assert.AreEqual(1, _result.MergedMap.PrepareCheckpoint);
	}

	[Test]
	public void the_commit_checkpoint_is_taken_from_the_latest_added_table() {
		Assert.AreEqual(2, _result.MergedMap.CommitCheckpoint);
	}

	[Test]
	public void there_are_eight_items_to_delete() {
		Assert.AreEqual(8, _result.ToDelete.Count);
	}

	[Test]
	public void the_merged_map_has_a_single_file() {
		Assert.AreEqual(1, _result.MergedMap.GetAllFilenames().Count());
		Assert.AreEqual(_finalmergefile2, _result.MergedMap.GetAllFilenames().ToList()[0]);
	}

	[Test]
	public void the_original_map_did_not_change() {
		Assert.AreEqual(0, _map.InOrder().Count());
		Assert.AreEqual(0, _map.GetAllFilenames().Count());
	}

	[Test]
	public void a_merged_file_was_created() {
		Assert.IsTrue(File.Exists(_finalmergefile2));
	}
}
