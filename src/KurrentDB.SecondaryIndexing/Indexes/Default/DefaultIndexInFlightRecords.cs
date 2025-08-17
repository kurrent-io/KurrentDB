// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

record struct InFlightRecord(
	long LogPosition,
	string Category,
	string EventType,
	bool IsDeleted = false
);

class DefaultIndexInFlightRecords(SecondaryIndexingPluginOptions options) {
	readonly InFlightRecord[] _records = new InFlightRecord[options.CommitBatchSize];

	public int Count { get; private set; }

	public void Append(InFlightRecord record) {
		_records[Count++] = record;
	}

	public void Clear() {
		Count = 0;
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsForwards(
		TFPos startPosition,
		List<IndexQueryRecord> fromDb,
		int maxCount,
		Func<InFlightRecord, bool>? query = null
	) {
		var remaining = maxCount - fromDb.Count;
		var from = fromDb.Count == 0 ? startPosition.PreparePosition : fromDb[^1].LogPosition;
		var seq = fromDb.Count == 0 ? 0 : fromDb[^1].RowId + 1;
		for (var i = 0; i < Count; i++) {
			if (remaining == 0) yield break;
			var current = _records[i];
			if (current.LogPosition >= from && (query == null || query(current))) {
				remaining--;
				yield return new(seq++, current.LogPosition);
			}
		}
	}

	public IEnumerable<IndexQueryRecord> GetInFlightRecordsBackwards(
		TFPos startPosition,
		int maxCount,
		Func<InFlightRecord, bool>? query = null
	) {
		if (Count == 0 || _records[0].LogPosition > startPosition.PreparePosition) {
			yield break;
		}

		var seq = -maxCount - 1;
		var remaining = maxCount;
		for (var i = Count - 1; i >= 0; i--) {
			if (remaining == 0) yield break;
			var current = _records[i];
			if (current.LogPosition <= startPosition.PreparePosition && (query == null || query(current))) {
				remaining--;
				yield return new(seq++, current.LogPosition);
			}
		}
	}
}
