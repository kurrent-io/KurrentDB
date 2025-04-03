// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Util;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2)]
[TestFixture(PTableVersions.IndexV3)]
[TestFixture(PTableVersions.IndexV4)]
public class saving_index_with_single_item_to_a_file : SpecificationWithDirectoryPerTestFixture {
	private string _filename;
	private IndexMap _map;
	private string _tablename;
	private string _mergeFile;
	private MergeResult _result;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private int _maxAutoMergeIndexLevel = 4;

	public saving_index_with_single_item_to_a_file(byte version) {
		_ptableVersion = version;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_filename = GetFilePathFor("indexfile");
		_tablename = GetTempFilePath();
		_mergeFile = GetFilePathFor("outputfile");

		_map = IndexMapTestFactory.FromFile(_filename, maxAutoMergeLevel: _maxAutoMergeIndexLevel);
		var memtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memtable.Add(0, 2, 7);
		var table = PTable.FromMemtable(memtable, _tablename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault);
		_result = _map.AddAndMergePTable(table, 7, 11,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0);
		_result.MergedMap.SaveToFile(_filename);
		_result.ToDelete.ForEach(x => x.Dispose());
		_result.MergedMap.InOrder().ToList().ForEach(x => x.Dispose());
		table.Dispose();
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result.MergedMap.InOrder().ToList().ForEach(x => x.MarkForDestruction());
		_result.MergedMap.InOrder().ToList().ForEach(x => x.WaitForDisposal(1000));
		return base.TestFixtureTearDown();
	}

	[Test]
	public void the_file_exists() {
		Assert.IsTrue(File.Exists(_filename));
	}

	[Test]
	public void the_file_contains_correct_data() {
		using (var fs = File.OpenRead(_filename))
		using (var reader = new StreamReader(fs)) {
			var text = reader.ReadToEnd();
			var lines = text.Replace("\r", "").Split('\n');

			fs.Position = 32;
			var md5 = MD5Hash.GetHashFor(fs);
			var md5String = BitConverter.ToString(md5).Replace("-", "");

			Assert.AreEqual(6, lines.Count());
			Assert.AreEqual(md5String, lines[0]);
			Assert.AreEqual(_map.Version.ToString(), lines[1]);
			Assert.AreEqual("7/11", lines[2]);
			Assert.AreEqual(_maxAutoMergeIndexLevel.ToString(), lines[3]);
			Assert.AreEqual("0,0," + Path.GetFileName(_tablename), lines[4]);
			Assert.AreEqual("", lines[5]);
		}
	}

	[Test]
	public void saved_file_could_be_read_correctly_and_without_errors() {
		var map = IndexMapTestFactory.FromFile(_filename, maxAutoMergeLevel: _maxAutoMergeIndexLevel);
		map.InOrder().ToList().ForEach(x => x.Dispose());

		Assert.AreEqual(7, map.PrepareCheckpoint);
		Assert.AreEqual(11, map.CommitCheckpoint);
	}
}
