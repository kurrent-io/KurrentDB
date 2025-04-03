// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using EventStore.Core.Util;
using NUnit.Framework;
using EventStore.Common.Utils;

namespace EventStore.Core.Tests.Index.IndexV1;

[TestFixture(PTableVersions.IndexV2)]
[TestFixture(PTableVersions.IndexV3)]
[TestFixture(PTableVersions.IndexV4)]
public class index_map_should : SpecificationWithDirectory {
	private string _indexMapFileName;
	private string _ptableFileName;
	private IndexMap _emptyIndexMap;
	private PTable _ptable;
	protected byte _ptableVersion = PTableVersions.IndexV1;

	public index_map_should(byte version) {
		_ptableVersion = version;
	}

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();

		_indexMapFileName = GetFilePathFor("index.map");
		_ptableFileName = GetFilePathFor("ptable");

		_emptyIndexMap = IndexMapTestFactory.FromFile(_indexMapFileName);

		var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memTable.Add(0, 1, 2);
		_ptable = PTable.FromMemtable(memTable, _ptableFileName, Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault);
	}

	[TearDown]
	public override Task TearDown() {
		_ptable.MarkForDestruction();
		return base.TearDown();
	}

	[Test]
	public void not_allow_negative_prepare_checkpoint_when_adding_ptable() {
		Assert.Throws<ArgumentOutOfRangeException>(() => _emptyIndexMap.AddPTable(_ptable, -1, 0));
	}

	[Test]
	public void not_allow_negative_commit_checkpoint_when_adding_ptable() {
		Assert.Throws<ArgumentOutOfRangeException>(() => _emptyIndexMap.AddPTable(_ptable, 0, -1));
	}

	[Test]
	public void throw_corruptedindexexception_when_prepare_checkpoint_is_less_than_minus_one() {
		CreateArtificialIndexMapFile(_indexMapFileName, -2, 0, null);
		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void allow_prepare_checkpoint_equal_to_minus_one_if_no_ptables_are_in_index() {
		CreateArtificialIndexMapFile(_indexMapFileName, -1, 0, null);
		Assert.DoesNotThrow(() => {
			var indexMap = IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2);
			indexMap.InOrder().ToList().ForEach(x => x.Dispose());
		});
	}

	[Test]
	public void
		throw_corruptedindexexception_if_prepare_checkpoint_is_minus_one_and_there_are_ptables_in_indexmap() {
		CreateArtificialIndexMapFile(_indexMapFileName, -1, 0, _ptableFileName);
		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void throw_corruptedindexexception_when_commit_checkpoint_is_less_than_minus_one() {
		CreateArtificialIndexMapFile(_indexMapFileName, 0, -2, null);
		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	[Test]
	public void allow_commit_checkpoint_equal_to_minus_one_if_no_ptables_are_in_index() {
		CreateArtificialIndexMapFile(_indexMapFileName, 0, -1, null);
		Assert.DoesNotThrow(() => {
			var indexMap = IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2);
			indexMap.InOrder().ToList().ForEach(x => x.Dispose());
		});
	}

	[Test]
	public void
		throw_corruptedindexexception_if_commit_checkpoint_is_minus_one_and_there_are_ptables_in_indexmap() {
		CreateArtificialIndexMapFile(_indexMapFileName, 0, -1, _ptableFileName);
		Assert.Throws<CorruptIndexException>(() =>
			IndexMapTestFactory.FromFile(_indexMapFileName, maxTablesPerLevel: 2));
	}

	private void CreateArtificialIndexMapFile(string filePath, long prepareCheckpoint, long commitCheckpoint,
		string ptablePath) {
		using (var memStream = new MemoryStream())
		using (var memWriter = new StreamWriter(memStream)) {
			memWriter.WriteLine(new string('0', 32)); // pre-allocate space for MD5 hash
			memWriter.WriteLine("1");
			memWriter.WriteLine("{0}/{1}", prepareCheckpoint, commitCheckpoint);
			if (!string.IsNullOrWhiteSpace(ptablePath)) {
				memWriter.WriteLine("0,0,{0}", ptablePath);
			}

			memWriter.Flush();

			using (var f = File.OpenWrite(filePath))
			using (var fileWriter = new StreamWriter(f)) {
				memStream.Position = 0;
				memStream.CopyTo(f);

				memStream.Position = 32;
				var hash = MD5Hash.GetHashFor(memStream);
				f.Position = 0;
				for (int i = 0; i < hash.Length; ++i) {
					fileWriter.Write(hash[i].ToString("X2"));
				}

				fileWriter.WriteLine();
				fileWriter.Flush();
				f.FlushToDisk();
			}
		}
	}
}
