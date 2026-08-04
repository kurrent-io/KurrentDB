// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using BenchmarkDotNet.Attributes;
using KurrentDB.Core.Index;

namespace KurrentDB.MicroBenchmarks.PTable;

// Benchmarks opening a PTable from disk. With index verification enabled the whole file is read
// and its MD5 recomputed; with it disabled the midpoints are rebuilt without hashing (and, for V4,
// loaded straight from the file's cached midpoints), so the two modes bracket the range of open
// costs.
[MemoryDiagnoser]
public class PTableReadBenchmarks {
	[Params(1_000_000, 16_000_000)]
	public int IndexSize;

	[Params(8, 16, 20)]
	public int CacheDepth;

	[Params(false, true)]
	public bool SkipIndexVerify;

	[Params(PTableVersions.IndexV2, PTableVersions.IndexV3, PTableVersions.IndexV4)]
	public byte Version;

	private PTableBenchmarkData _data;
	private string _file;

	[GlobalSetup]
	public void Setup() {
		_data = PTableBenchmarkData.Generate(IndexSize);
		_file = _data.CreateFile(Version, cacheDepth: CacheDepth);
	}

	[GlobalCleanup]
	public void Cleanup() => _data.DeleteFile(Version);

	[Benchmark]
	public long OpenFromFile() {
		var table = Core.Index.PTable.FromFile(
			_file,
			PTableBenchmarkData.InitialReaders,
			PTableBenchmarkData.MaxReaders,
			cacheDepth: CacheDepth,
			skipIndexVerify: SkipIndexVerify
		);
		var count = table.Count;
		table.Dispose();
		table.WaitForDisposal(TimeSpan.FromSeconds(120));
		return count;
	}
}
