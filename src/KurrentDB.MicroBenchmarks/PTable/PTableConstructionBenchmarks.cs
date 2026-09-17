// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using BenchmarkDotNet.Attributes;
using KurrentDB.Core.Index;

namespace KurrentDB.MicroBenchmarks.PTable;

// Benchmarks writing a PTable to disk from an in-memory memtable. The memtable is built once in
// the setup so only the dump (index entries + midpoints + bloom filter + MD5) is measured.
[MemoryDiagnoser]
public class PTableConstructionBenchmarks {
	[Params(1_000_000, 16_000_000)]
	public int IndexSize;

	[Params(PTableVersions.IndexV2, PTableVersions.IndexV3, PTableVersions.IndexV4)]
	public byte Version;

	private PTableBenchmarkData _data;
	private HashListMemTable _memTable;
	private string _outputFile;

	[GlobalSetup]
	public void Setup() {
		_data = PTableBenchmarkData.Generate(IndexSize);
		_memTable = _data.BuildMemTable(Version);
		_outputFile = _data.FilePath(Version);
		_data.DeleteFile(Version);
	}

	[Benchmark]
	public void FromMemtable() {
		// FromMemtable creates the file with FileMode.Create, so reusing the same path just
		// overwrites the previous invocation's output.
		var table = Core.Index.PTable.FromMemtable(
			_memTable,
			_outputFile,
			PTableBenchmarkData.InitialReaders,
			PTableBenchmarkData.MaxReaders,
			skipIndexVerify: true);
		table.Dispose();
		table.WaitForDisposal(TimeSpan.FromSeconds(120));
	}

	[GlobalCleanup]
	public void Cleanup() => _data.DeleteFile(Version);
}
