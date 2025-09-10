// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal record struct InFlightRecord(
	long LogPosition,
	string Category,
	string EventType,
	string StreamName,
	long EventNumber,
	long Created
);

internal class DefaultIndexInFlightRecords(SecondaryIndexingPluginOptions options) {
	private readonly InFlightRecord[] _records = new InFlightRecord[options.CommitBatchSize];

	private uint _version; // used for optimistic lock
	private int _count;

	public int Count => _count;

	public void Append(long logPosition, string category, string eventType, string stream, long eventNumber, long created) {
		var count = _count;
		_records[count] = new(logPosition, category, eventType, stream, eventNumber, created);

		// Fence: make sure that the array modification cannot be done after the increment
		Volatile.Write(ref _count, count + 1);
	}

	public void Clear() {
		Interlocked.Increment(ref _version); // full fence

		// Fence: make sure that the count is modified after the version
		_count = 0;
	}

	// read is protected by optimistic lock
	private bool TryRead(uint currentVer, int index, out InFlightRecord record) {
		record = _records[index];

		// ensure that the record is copied before the comparison
		Interlocked.MemoryBarrier();
		return currentVer == _version;
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsForwards(
		long startPosition,
		int maxCount,
		bool excludeFirst,
		Func<InFlightRecord, bool>? query = null) {
		query ??= True; // to avoid branching in the loop

		bool first = true;

		var currentVer = _version;
		for (int i = 0, count = Volatile.Read(in _count), remaining = maxCount;
		     i < count && remaining > 0 && TryRead(currentVer, i, out var current);
		     i++) {
			if (current.LogPosition >= startPosition && query(current)) {
				if (first && excludeFirst && current.LogPosition == startPosition) {
					first = false;
					continue;
				}
				remaining--;
				yield return new(current.LogPosition, current.EventNumber);
			}
		}
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsBackwards(
		long startPosition,
		int maxCount,
		bool excludeFirst,
		Func<InFlightRecord, bool>? query = null) {
		query ??= True; // to avoid branching in the loop

		var count = Volatile.Read(in _count);
		var currentVer = _version;
		bool first = true;

		if (count > 0
		    && TryRead(currentVer, 0, out var current)
		    && current.LogPosition <= startPosition) {
			for (int i = count - 1, remaining = maxCount;
			     i >= 0 && remaining > 0 && TryRead(currentVer, i, out current);
			     i--) {
				if (current.LogPosition <= startPosition && query(current)) {
					if (first && excludeFirst && current.LogPosition == startPosition) {
						first = false;
						continue;
					}
					remaining--;
					yield return new(current.LogPosition, current.EventNumber);
				}
			}
		}
	}

	private static bool True(InFlightRecord record) => true;

	public IEnumerable<InFlightRecord> GetInFlightRecords() {
		var currentVer = _version;
		for (int i = 0, count = Volatile.Read(in _count);
		     i < count && TryRead(currentVer, i, out var current);
		     i++) {
			yield return current;
		}
	}
}
