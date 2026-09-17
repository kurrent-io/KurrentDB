// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using BenchmarkDotNet.Attributes;

namespace KurrentDB.MicroBenchmarks.PTable;

// Completely random point lookups: every query targets a random stream at a random, non-sequential
// position in the index.
//   - Latest        looks up each stream's newest event  (TryGetLatestEntry, uses the LRU cache)
//   - SpecificEvent looks up a random existing event      (TryGetOneValue, binary chop, no LRU)
[ShortRunJob]
[MemoryDiagnoser]
public class PTableRandomQueryBenchmarks : PTableQueryBenchmarksBase {
	protected override PTableQuery[] BuildWorkload(PTableBenchmarkData data) =>
		data.BuildRandomWorkload(Load, QueriesPerInvocation);

	[Benchmark]
	public long Latest() => RunParallel(LatestRange);

	[Benchmark]
	public long SpecificEvent() => RunParallel(SpecificEventRange);

	// Accumulates the found positions so the lookups can't be optimized away.
	private long LatestRange(int start, int end) {
		long acc = 0;
		var workload = Workload;
		for (var i = start; i < end; i++) {
			if (Table.TryGetLatestEntry(workload[i].Stream, out var entry))
				acc += entry.Position;
		}

		return acc;
	}

	private long SpecificEventRange(int start, int end) {
		long acc = 0;
		var workload = Workload;
		for (var i = start; i < end; i++) {
			if (Table.TryGetOneValue(workload[i].Stream, workload[i].EventNumber, out var position))
				acc += position;
		}

		return acc;
	}
}
