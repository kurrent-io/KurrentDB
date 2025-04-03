// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Tests.Services.Storage.Transactions;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.AutoMergeLevelTests;

[TestFixture]
public abstract class when_max_auto_merge_level_is_set : SpecificationWithDirectoryPerTestFixture {
	protected readonly int _maxAutoMergeLevel;
	protected string _filename;
	protected IndexMap _map;
	protected byte _ptableVersion = 4;
	protected MergeResult _result;
	protected bool _skipIndexVerify = true;
	protected GuidFilenameProvider _fileNameProvider;

	public when_max_auto_merge_level_is_set(int maxAutoMergeLevel = 2) {
		_maxAutoMergeLevel = maxAutoMergeLevel;
	}

	[OneTimeSetUp]
	public virtual void Setup() {
		_filename = GetTempFilePath();
		_fileNameProvider = new GuidFilenameProvider(PathName);
		_map = IndexMapTestFactory.FromFile(_filename, maxTablesPerLevel: 2, maxAutoMergeLevel: _maxAutoMergeLevel);
	}

	protected void AddTables(int count) {
		var memtable = new HashListMemTable(_ptableVersion, maxSize: 10);
		memtable.Add(0, 1, 0);
		var first = _map;
		if (_result != null)
			first = _result.MergedMap;
		var pTable = PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify);
		_result = first.AddAndMergePTable(pTable,
			10, 20, _fileNameProvider, _ptableVersion,0, skipIndexVerify: _skipIndexVerify);
		for (int i = 3; i <= count * 2; i += 2) {
			pTable = PTable.FromMemtable(memtable, GetTempFilePath(), Constants.PTableInitialReaderCount, Constants.PTableMaxReaderCountDefault, skipIndexVerify: _skipIndexVerify);
			_result = _result.MergedMap.AddAndMergePTable(
				pTable,
				prepareCheckpoint: i * 10,
				commitCheckpoint: (i + 1) * 10,
				_fileNameProvider, _ptableVersion, 0, skipIndexVerify: _skipIndexVerify);
			_result.ToDelete.ForEach(x => x.MarkForDestruction());
		}
	}

	[OneTimeTearDown]
	public override Task TestFixtureTearDown() {
		_result.ToDelete.ForEach(x => x.MarkForDestruction());
		_result.MergedMap.InOrder().ToList().ForEach(x => x.MarkForDestruction());
		_result.MergedMap.Dispose(TimeSpan.FromMilliseconds(100));
		_map.Dispose(TimeSpan.FromMilliseconds(100));
		File.Delete(_filename);

		return base.TestFixtureTearDown();
	}
}
