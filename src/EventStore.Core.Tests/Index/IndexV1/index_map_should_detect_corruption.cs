// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
public class index_map_should_detect_corruption : SpecificationWithDirectory {
	private string _indexMapFileName;
	private string _ptableFileName;
	private PTable _ptable;
	protected byte _ptableVersion = PTableVersions.IndexV1;
	private bool _skipIndexVerify;
	private int _maxAutoMergeIndexLevel = 4;

	public index_map_should_detect_corruption(byte version, bool skipIndexVerify) {
		_ptableVersion = version;
		_skipIndexVerify = skipIndexVerify;
	}

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();

		_indexMapFileName = GetFilePathFor("index.map");
		_ptableFileName = GetFilePathFor("ptable");

		var indexMap = IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2, maxAutoMergeLevel: _maxAutoMergeIndexLevel);
		var memtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memtable.Add(0, 0, 0);
		memtable.Add(1, 1, 100);
		_ptable = PTable.FromMemtable(memtable, _ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify);

		indexMap = indexMap.AddAndMergePTable(_ptable, 0, 0,
			new GuidFilenameProvider(PathName), _ptableVersion, 0, skipIndexVerify: _skipIndexVerify).MergedMap;
		indexMap.SaveToFile(_indexMapFileName);
	}

	[TearDown]
	public override Task TearDown() {
		_ptable?.MarkForDestruction();
		return base.TearDown();
	}

	[Test]
	public void when_ptable_file_is_deleted() {
		_ptable.MarkForDestruction();
		_ptable = null;
		File.Delete(_ptableFileName);

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_indexmap_file_does_not_have_md5_checksum() {
		var lines = File.ReadAllLines(_indexMapFileName);
		File.WriteAllLines(_indexMapFileName, lines.Skip(1));

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_indexmap_file_does_not_have_latest_commit_position() {
		var lines = File.ReadAllLines(_indexMapFileName);
		File.WriteAllLines(_indexMapFileName, lines.Where((x, i) => i != 1));

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_indexmap_file_exists_but_is_empty() {
		File.WriteAllText(_indexMapFileName, "");

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_indexmap_file_is_garbage() {
		File.WriteAllText(_indexMapFileName, "alkfjasd;lkf\nasdfasdf\n");

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_checkpoints_pair_is_corrupted() {
		using (var fs = File.Open(_indexMapFileName, FileMode.Open)) {
			fs.Position = 34;
			var b = (byte)fs.ReadByte();
			b ^= 1;
			fs.Position = 34;
			fs.WriteByte(b);
		}

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_ptable_line_is_missing_one_number() {
		var lines = File.ReadAllLines(_indexMapFileName);
		File.WriteAllLines(_indexMapFileName, new[] { lines[0], lines[1], string.Format("0,{0}", _ptableFileName) });

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_ptable_line_constists_only_of_filename() {
		var lines = File.ReadAllLines(_indexMapFileName);
		File.WriteAllLines(_indexMapFileName, new[] { lines[0], lines[1], _ptableFileName });

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_ptable_line_is_missing_filename() {
		var lines = File.ReadAllLines(_indexMapFileName);
		File.WriteAllLines(_indexMapFileName, new[] { lines[0], lines[1], "0,0" });

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_indexmap_md5_checksum_is_corrupted() {
		using (var fs = File.Open(_indexMapFileName, FileMode.Open)) {
			var b = (byte)fs.ReadByte();
			b ^= 1; // swap single bit
			fs.Position = 0;
			fs.WriteByte(b);
		}

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_ptable_hash_is_corrupted() {
		_ptable.Dispose();
		_ptable = null;

		using (var fs = File.Open(_ptableFileName, FileMode.Open)) {
			fs.Seek(-PTable.MD5Size, SeekOrigin.End);
			var b = (byte)fs.ReadByte();
			b ^= 1;
			fs.Seek(-PTable.MD5Size, SeekOrigin.End);
			fs.WriteByte(b);
		}

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_ptable_type_is_corrupted() {
		_ptable.Dispose();
		_ptable = null;

		using (var fs = File.Open(_ptableFileName, FileMode.Open)) {
			fs.Seek(0, SeekOrigin.Begin);
			fs.WriteByte(123);
		}

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_ptable_header_is_corrupted() {
		_ptable.Dispose();
		_ptable = null;

		using (var fs = File.Open(_ptableFileName, FileMode.Open)) {
			fs.Position = new Random().Next(0, PTableHeader.Size);
			var b = (byte)fs.ReadByte();
			b ^= 1;
			fs.Position -= 1;
			fs.WriteByte(b);
		}

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void when_ptable_data_is_corrupted() {
		_ptable.Dispose();
		_ptable = null;

		using (var fs = File.Open(_ptableFileName, FileMode.Open)) {
			fs.Position = new Random().Next(PTableHeader.Size, (int)fs.Length);
			var b = (byte)fs.ReadByte();
			b ^= 1;
			fs.Position -= 1;
			fs.WriteByte(b);
		}

		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}
}
