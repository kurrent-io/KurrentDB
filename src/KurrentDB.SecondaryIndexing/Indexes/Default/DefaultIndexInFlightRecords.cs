// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

[StructLayout(LayoutKind.Auto)]
readonly record struct InFlightRecord(
	long LogPosition,
	int CategoryId,
	int EventTypeId
);

class DefaultIndexInFlightRecords(SecondaryIndexingPluginOptions options) {
	readonly InFlightRecord[] _records = new InFlightRecord[options.CommitBatchSize];
	private uint _version; // used for optimistic lock
	private int _count;

	public int Count => _count;

	public void Append(long logPosition, int categoryId, int eventTypeId) {
		var count = _count;
		_records[count] = new(logPosition, categoryId, eventTypeId);

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
		TFPos startPosition,
		int maxCount,
		bool excludeFirst,
		Func<InFlightRecord, bool>? query = null) {
		query ??= True; // to avoid branching in the loop

		var from = startPosition.PreparePosition + Unsafe.BitCast<bool, byte>(excludeFirst);
		var currentVer = _version;

		for (int i = 0, count = Volatile.Read(in _count), seq = 0;
		     i < count && maxCount > 0 && TryRead(currentVer, i, out var current);
		     i++, maxCount--) {

			// make sure that the obtained record is not dirty due to concurrent write. Otherwise,
			// 'current' variable is not valid
			if (current.LogPosition >= from && query.Invoke(current))
				yield return new(seq++, current.LogPosition);
		}
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsBackwards(
		TFPos startPosition,
		int maxCount,
		bool excludeFirst,
		Func<InFlightRecord, bool>? query = null) {
		query ??= True; // to avoid branching in the loop

		var from = startPosition.PreparePosition - Unsafe.BitCast<bool, byte>(excludeFirst);
		var currentVer = _version;

		for (int count = Volatile.Read(in _count), i = count - 1, seq = 0;
		     i >= 0 && maxCount > 0 && TryRead(currentVer, i, out var current);
		     i--, maxCount--) {

			// make sure that the obtained record is not dirty due to concurrent write. Otherwise,
			// 'current' variable is not valid
			if (current.LogPosition <= from && query.Invoke(current))
				yield return new(seq++, current.LogPosition);
		}
	}

	private static bool True(InFlightRecord record) => true;
}
