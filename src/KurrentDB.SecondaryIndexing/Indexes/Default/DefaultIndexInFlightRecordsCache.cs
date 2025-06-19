// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexInFlightRecordsCache(SecondaryIndexingPluginOptions options) {
	private readonly InFlightRecord[] _records = new InFlightRecord[options.CommitBatchSize];
	public int Count { get; private set; }

	public void Append(InFlightRecord record) {
		_records[Count++] = record;
	}

	public void Clear() {
		Count = 0;
	}

	public IEnumerable<AllRecord> TryGetInFlightRecords(long fromSeq, long toSeq) {
		for (var i = 0; i < Count; i++) {
			var current = _records[i];
			if (current.Seq > toSeq) yield break;
			if (current.Seq >= fromSeq && current.Seq <= toSeq) {
				yield return new(current.Seq, current.LogPosition);
			}
		}
	}

	public IEnumerable<T> QueryInFlightRecords<T>(Func<InFlightRecord, bool> query, Func<InFlightRecord, T> map) {
		for (var i = 0; i < Count; i++) {
			var current = _records[i];
			if (query(current)) yield return map(current);
		}
	}
}
