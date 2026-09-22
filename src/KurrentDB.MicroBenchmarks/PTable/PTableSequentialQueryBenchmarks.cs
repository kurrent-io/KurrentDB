// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using BenchmarkDotNet.Attributes;

namespace KurrentDB.MicroBenchmarks.PTable;

// Sequential-within-a-stream point lookups (TryGetOneValue). Streams are visited in random order,
// but each visited stream's events are read in order, modelling reading a stream:
//   - Forward  reads events 0, 1, 2, ...   (the workload as generated)
//   - Backward reads events ..., 2, 1, 0   (the same workload consumed in reverse)
[ShortRunJob]
[MemoryDiagnoser]
public class PTableSequentialQueryBenchmarks : PTableQueryBenchmarksBase {
	protected override PTableQuery[] BuildWorkload(PTableBenchmarkData data) =>
		data.BuildSequentialWorkload(Load, QueriesPerInvocation);

	[Benchmark]
	public long Forward() => RunParallel(ForwardRange);

	[Benchmark]
	public long Backward() => RunParallel(BackwardRange);

	// Accumulates the found positions so the lookups can't be optimized away.
	private long ForwardRange(int start, int end) {
		long acc = 0;
		var workload = Workload;
		for (var i = start; i < end; i++) {
			if (Table.TryGetOneValue(workload[i].Stream, workload[i].EventNumber, out var position))
				acc += position;
		}

		return acc;
	}

	private long BackwardRange(int start, int end) {
		long acc = 0;
		var workload = Workload;
		for (var i = end - 1; i >= start; i--) {
			if (Table.TryGetOneValue(workload[i].Stream, workload[i].EventNumber, out var position))
				acc += position;
		}

		return acc;
	}
}
