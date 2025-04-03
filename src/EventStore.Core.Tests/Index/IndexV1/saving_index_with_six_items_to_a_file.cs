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

[TestFixture(PTableVersions.IndexV3)]
[TestFixture(PTableVersions.IndexV4)]
public class saving_index_with_six_items_to_a_file : SpecificationWithDirectory {
	private string _filename;
	private string _tablename;
	private string _mergeFile;
	private IndexMap _map;
	private MergeResult _result;
	protected byte _ptableVersion = PTableVersions.IndexV1;

	public saving_index_with_six_items_to_a_file(byte version) {
		_ptableVersion = version;
	}

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();

		_filename = GetFilePathFor("indexfile");
		_tablename = GetTempFilePath();
		_mergeFile = GetFilePathFor("outfile");

		_map = IndexMapTestFactory.FromFile(_filename, maxTablesPerLevel: 4);
		var memtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memtable.Add(0, 2, 123);
		var table = PTable.FromMemtable(memtable, _tablename, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault);
		_result = _map.AddAndMergePTable(table, 0, 0,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0);
		_result = _result.MergedMap.AddAndMergePTable(table, 0, 0,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0);
		_result = _result.MergedMap.AddAndMergePTable(table, 0, 0,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0);
		var merged = _result.MergedMap.AddAndMergePTable(table, 0, 0,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0);
		_result = merged.MergedMap.AddAndMergePTable(table, 0, 0,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0);
		_result = _result.MergedMap.AddAndMergePTable(table, 7, 11,
			new FakeFilenameProvider(_mergeFile), _ptableVersion, 0);
		_result.MergedMap.SaveToFile(_filename);

		table.Dispose();

		merged.MergedMap.InOrder().ToList().ForEach(x => x.Dispose());
		merged.ToDelete.ForEach(x => x.Dispose());

		_result.MergedMap.InOrder().ToList().ForEach(x => x.Dispose());
		_result.ToDelete.ForEach(x => x.Dispose());
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

			Assert.AreEqual(8, lines.Count());
			Assert.AreEqual(md5String, lines[0]);
			Assert.AreEqual(_map.Version.ToString(), lines[1]);
			Assert.AreEqual("7/11", lines[2]);
			Assert.AreEqual(int.MaxValue.ToString(), lines[3]);
			var name = new FileInfo(_tablename).Name;
			Assert.AreEqual("0,0," + name, lines[4]);
			Assert.AreEqual("0,1," + name, lines[5]);
			Assert.AreEqual("1,0," + Path.GetFileName(_mergeFile), lines[6]);
			Assert.AreEqual("", lines[7]);
		}
	}

	[Test]
	public void saved_file_could_be_read_correctly_and_without_errors() {
		var map = IndexMapTestFactory.FromFile(_filename);
		map.InOrder().ToList().ForEach(x => x.Dispose());

		Assert.AreEqual(7, map.PrepareCheckpoint);
		Assert.AreEqual(11, map.CommitCheckpoint);
	}
}
