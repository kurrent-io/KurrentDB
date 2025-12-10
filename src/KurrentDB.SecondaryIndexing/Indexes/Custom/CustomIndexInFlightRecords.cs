// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal record struct CustomIndexInFlightRecord(
	long LogPosition,
	long CommitPosition,
	long EventNumber,
	long Created,
	IField? Field
);

internal class CustomIndexInFlightRecords(SecondaryIndexingPluginOptions options) {
	private readonly CustomIndexInFlightRecord[] _records = new CustomIndexInFlightRecord[options.CommitBatchSize];

	private uint _version; // used for optimistic lock
	private int _count;

	public int Count => _count;

	public void Append(long logPosition, long commitPosition, long eventNumber, IField? field, long created) {
		var count = _count;
		_records[count] = new CustomIndexInFlightRecord {
			LogPosition = logPosition,
			CommitPosition = commitPosition,
			EventNumber = eventNumber,
			Field = field,
			Created = created
		};

		// Fence: make sure that the array modification cannot be done after the increment
		Volatile.Write(ref _count, count + 1);
	}

	public void Clear() {
		Interlocked.Increment(ref _version); // full fence

		// Fence: make sure that the count is modified after the version
		_count = 0;
	}

	// read is protected by optimistic lock
	private bool TryRead(uint currentVer, int index, out CustomIndexInFlightRecord record) {
		record = _records[index];

		// ensure that the record is copied before the comparison
		Interlocked.MemoryBarrier();
		return currentVer == _version;
	}

	public (List<IndexQueryRecord>, bool) GetInFlightRecordsForwards(
		long startPosition,
		int maxCount,
		bool excludeFirst,
		Func<CustomIndexInFlightRecord, bool>? query = null) {
		query ??= True; // to avoid branching in the loop

		var isComplete = false;
		bool first = true;

		var currentVer = _version;
		var result = new List<IndexQueryRecord>();
		for (int i = 0, count = Volatile.Read(in _count), remaining = maxCount;
			 i < count && remaining > 0 && TryRead(currentVer, i, out var current);
			 i++) {
			if (current.LogPosition >= startPosition) {
				if (i == 0 && current.LogPosition == startPosition) {
					isComplete = true;
				}
				if (query(current)) {
					if (first && excludeFirst && current.LogPosition == startPosition) {
						first = false;
						continue;
					}

					remaining--;
					result.Add(new(current.LogPosition, current.CommitPosition, current.EventNumber));
				}
			} else {
				if (i == 0) {
					isComplete = true;
				}
			}
		}

		return (result, isComplete);
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsBackwards(
		long startPosition,
		int maxCount,
		bool excludeFirst,
		Func<CustomIndexInFlightRecord, bool>? query = null) {
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
					yield return new(current.LogPosition, current.CommitPosition, current.EventNumber);
				}
			}
		}
	}

	private static bool True(CustomIndexInFlightRecord record) => true;

	public IEnumerable<CustomIndexInFlightRecord> GetInFlightRecords() {
		var currentVer = _version;
		for (int i = 0, count = Volatile.Read(in _count);
			 i < count && TryRead(currentVer, i, out var current);
			 i++) {
			yield return current;
		}
	}
}
