// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

record struct InFlightRecord(
	long LogPosition,
	int CategoryId,
	int EventTypeId,
	bool IsDeleted = false
);

class DefaultIndexInFlightRecords(SecondaryIndexingPluginOptions options) {
	readonly InFlightRecord[] _records = new InFlightRecord[options.CommitBatchSize];
	private long _batchCounter; // for synchronization
	private volatile int _count;

	public int Count => _count;

	public void Append(InFlightRecord record) {
		_records[_count++] = record;
	}

	public void Clear() {
		_count = 0;
		Volatile.Write(ref _batchCounter, _batchCounter + 1);
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsForwards(
		TFPos startPosition,
		int maxCount,
		bool excludeFirst,
		Func<InFlightRecord, bool>? query = null) {
		var initialBatchCounter = Volatile.Read(ref _batchCounter);

		var from = startPosition.PreparePosition + (excludeFirst ? 1 : 0);
		var seq = 0;

		var count = _count;
		for (var i = 0; i < count; i++) {
			if (maxCount == 0)
				yield break;

			var current = _records[i];
			if (current.LogPosition < from)
				continue;

			if (query is not null && !query(current))
				continue;

			if (Volatile.Read(ref _batchCounter) != initialBatchCounter)
				break;

			yield return new(seq++, current.LogPosition);
			maxCount--;
		}
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsBackwards(
		TFPos startPosition,
		int maxCount,
		bool excludeFirst,
		Func<InFlightRecord, bool>? query = null) {
		var initialBatchCounter = Volatile.Read(ref _batchCounter);

		var from = startPosition.PreparePosition - (excludeFirst ? 1 : 0);
		var seq = 0;

		var count = _count;
		for (var i = count - 1; i >= 0; i--) {
			if (maxCount == 0)
				yield break;

			var current = _records[i];
			if (current.LogPosition > from)
				continue;

			if (query is not null && !query(current))
				continue;

			if (Volatile.Read(ref _batchCounter) != initialBatchCounter)
				break;

			yield return new(seq++, current.LogPosition);
			maxCount--;
		}
	}
}
