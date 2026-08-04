// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using KurrentDB.Core.Index;
using KurrentDB.Core.Settings;

namespace KurrentDB.MicroBenchmarks.PTable;

public enum PTableLoad {
	// A small pool of distinct streams queried repeatedly at random.
	SmallSubset,

	// Random access spread across (the majority of) all streams.
	Majority,
}

public readonly record struct PTableQuery(ulong Stream, long EventNumber);

// The streams a benchmark index is made of: a hash per stream and the number of events it holds
// (event numbers 0, 1, 2, ... up to that count). Generated from a seeded RNG once per benchmark
// setup and reused to build the index file and every workload that runs against it, so the queries
// always reference entries that exist in the index and every run measures the same lookups.
public sealed class PTableBenchmarkData {
	// Number of events per stream, drawn uniformly from this (inclusive) range.
	private const int MinEventsPerStream = 3;
	private const int MaxEventsPerStream = 10;

	// Distinct streams the SmallSubset load draws from.
	private const int SmallSubsetStreams = 250;

	private const int StreamsSeed = 1;
	private const int WorkloadSeed = 2;

	public const int InitialReaders = ESConsts.PTableInitialReaderCount;
	public const int MaxReaders = 16;

	public static readonly string WorkDir =
		Path.Combine(Path.GetTempPath(), "KurrentDB.PTableBenchmarks");

	static PTableBenchmarkData() => Directory.CreateDirectory(WorkDir);

	private readonly ulong[] _hashes;
	private readonly int[] _eventCounts;
	private readonly int _entryCount;

	private PTableBenchmarkData(ulong[] hashes, int[] eventCounts, int entryCount) {
		_hashes = hashes;
		_eventCounts = eventCounts;
		_entryCount = entryCount;
	}

	public static PTableBenchmarkData Generate(int entryCount) {
		var rng = new Random(StreamsSeed);
		var hashes = new List<ulong>();
		var eventCounts = new List<int>();
		var entries = 0;
		while (entries < entryCount) {
			// Random hashes mean consecutive streams land at unrelated positions in the
			// (hash-sorted) index, so picking a random stream picks a random position in it.
			hashes.Add(unchecked((ulong)rng.NextInt64(long.MinValue, long.MaxValue)));
			var events = rng.Next(MinEventsPerStream, MaxEventsPerStream + 1);
			eventCounts.Add(events);
			entries += events;
		}

		return new(hashes.ToArray(), eventCounts.ToArray(), entries);
	}

	public HashListMemTable BuildMemTable(byte version) {
		var table = new HashListMemTable(version, maxSize: _entryCount);
		long position = 0;
		for (var s = 0; s < _hashes.Length; s++) {
			for (long eventNumber = 0; eventNumber < _eventCounts[s]; eventNumber++)
				table.Add(_hashes[s], eventNumber, position++);
		}

		return table;
	}

	// The path this data set's index is written to. Named after the data, so a benchmark that writes
	// the file itself (rather than through CreateFile) still deletes it through DeleteFile.
	public string FilePath(byte version) => Path.Combine(WorkDir, $"ptable_v{version}_{_entryCount}.idx");

	// Builds the PTable file for the given version, overwriting any file left behind by a previous
	// run, and returns its path. Call DeleteFile when done with it.
	public string CreateFile(byte version, int cacheDepth) {
		var path = FilePath(version);
		var table = Core.Index.PTable.FromMemtable(
			BuildMemTable(version), path, InitialReaders, MaxReaders, cacheDepth: cacheDepth, skipIndexVerify: true);
		table.Dispose();
		table.WaitForDisposal(TimeSpan.FromSeconds(120));
		return path;
	}

	// Completely random point queries: each query independently picks a random stream and a random
	// (existing) event number within it, so the queries land at random, non-sequential positions in
	// the index.
	public PTableQuery[] BuildRandomWorkload(PTableLoad load, int queryCount) {
		var poolSize = PoolSize(load);
		var rng = new Random(WorkloadSeed);
		var workload = new PTableQuery[queryCount];
		for (var i = 0; i < workload.Length; i++) {
			var s = rng.Next(poolSize);
			workload[i] = new PTableQuery(_hashes[s], rng.Next(_eventCounts[s]));
		}

		return workload;
	}

	// Sequential-within-a-stream queries: streams are visited in random order (so the index is
	// still hit at random positions), but the queries for a given stream cover its events in order
	// (0, 1, 2, ...). Consumers read this array forward as-is, or in reverse for a backward read.
	public PTableQuery[] BuildSequentialWorkload(PTableLoad load, int queryCount) {
		var poolSize = PoolSize(load);
		var rng = new Random(WorkloadSeed);
		var workload = new PTableQuery[queryCount];
		var i = 0;
		while (i < workload.Length) {
			var s = rng.Next(poolSize);
			for (long eventNumber = 0; eventNumber < _eventCounts[s] && i < workload.Length; eventNumber++)
				workload[i++] = new PTableQuery(_hashes[s], eventNumber);
		}

		return workload;
	}

	// The number of streams a workload may draw from, depending on the load kind.
	private int PoolSize(PTableLoad load) =>
		load == PTableLoad.SmallSubset ? Math.Min(SmallSubsetStreams, _hashes.Length) : _hashes.Length;

	// Deletes the index file and its bloom filter. Best effort: a file left behind is only wasted
	// disk space, and the next run overwrites it anyway.
	public void DeleteFile(byte version) {
		var path = FilePath(version);
		Delete(path);
		Delete(Core.Index.PTable.GenBloomFilterFilename(path));

		static void Delete(string path) {
			try {
				if (File.Exists(path)) {
					File.SetAttributes(path, FileAttributes.Normal);
					File.Delete(path);
				}
			} catch {
				// ignored
			}
		}
	}
}
