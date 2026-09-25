// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using KurrentDB.Core.Index;

namespace KurrentDB.MicroBenchmarks.PTable;

// Shared setup and threading harness for the PTable query benchmarks. Each concrete subclass is a
// separate access pattern (so it can be run on its own) that supplies its workload and the
// benchmark methods that consume it.
//
// All subclasses share the same dimensions: index size, thread count, and query load (a small
// pool of hot keys vs. the majority of keys). The PTable file is built in the setup and deleted in
// the cleanup.
public abstract class PTableQueryBenchmarksBase {
	protected const int QueriesPerInvocation = 100_000;

	[Params(1_000_000, 16_000_000)]
	public int IndexSize;

	[Params(1, 4, 8)]
	public int Threads;

	[Params(PTableLoad.SmallSubset, PTableLoad.Majority)]
	public PTableLoad Load;

	[Params(PTableVersions.IndexV2, PTableVersions.IndexV3, PTableVersions.IndexV4)]
	public byte Version;

	protected Core.Index.PTable Table;
	protected PTableQuery[] Workload;
	private long[] _partialResults;
	private PTableBenchmarkData _data;

	[GlobalSetup]
	public void Setup() {
		_data = PTableBenchmarkData.Generate(IndexSize);
		Table = Core.Index.PTable.FromFile(
			_data.CreateFile(Version, cacheDepth: 16),
			PTableBenchmarkData.InitialReaders,
			PTableBenchmarkData.MaxReaders,
			cacheDepth: 16,
			skipIndexVerify: true);
		Workload = BuildWorkload(_data);
		_partialResults = new long[Threads];
	}

	protected abstract PTableQuery[] BuildWorkload(PTableBenchmarkData data);

	// Spreads the workload across the configured number of threads (one contiguous slice each) and
	// sums the per-thread accumulators. The range function is invoked once per thread, so it adds
	// no per-query overhead.
	protected long RunParallel(Func<int, int, long> queryRange) {
		var threads = Threads;
		if (threads == 1)
			return queryRange(0, Workload.Length);

		var chunk = Workload.Length / threads;
		var tasks = new Task[threads];
		for (var t = 0; t < threads; t++) {
			var index = t;
			var start = index * chunk;
			var end = index == threads - 1 ? Workload.Length : start + chunk;
			tasks[index] = Task.Run(() => _partialResults[index] = queryRange(start, end));
		}

		Task.WaitAll(tasks);

		long sum = 0;
		for (var t = 0; t < threads; t++)
			sum += _partialResults[t];
		return sum;
	}

	[GlobalCleanup]
	public void Cleanup() {
		Table?.Dispose();
		Table?.WaitForDisposal(TimeSpan.FromSeconds(120));
		_data.DeleteFile(Version);
	}
}
