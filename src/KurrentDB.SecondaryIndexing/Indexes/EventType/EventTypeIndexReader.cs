// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndexReader(
	DuckDBConnectionPool sharedPool,
	DefaultIndexProcessor processor,
	IReadIndex<string> index)
	: SecondaryIndexReaderBase(sharedPool, index) {
	protected override string GetId(string streamName) =>
		EventTypeIndex.TryParseEventType(streamName, out var eventTypeName) ? eventTypeName : string.Empty;

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool db,
		string? id,
		long startPosition,
		int maxCount,
		bool excludeFirst) {
		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection)) {
			using (processor.CaptureSnapshot(connection)) {
				if (excludeFirst) {
					connection
						.ExecuteQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexQueryExcl>(new(id!, startPosition, maxCount))
						.CopyTo(records);
				} else {
					connection
						.ExecuteQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexQueryIncl>(new(id!, startPosition,
							maxCount))
						.CopyTo(records);
				}
			}
		}

		return records;
	}

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool db,
		string? id,
		long startPosition,
		int maxCount,
		bool excludeFirst) {
		var records = new List<IndexQueryRecord>(maxCount);
		using (db.Rent(out var connection)) {
			using (processor.CaptureSnapshot(connection)) {
				if (excludeFirst) {
					connection
						.ExecuteQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexBackQueryExcl>(new(id!, startPosition, maxCount))
						.CopyTo(records);
				} else {
					connection
						.ExecuteQuery<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexBackQueryIncl>(new(id!,
							startPosition, maxCount))
						.CopyTo(records);
				}
			}
		}

		return records;
	}

	public override TFPos GetLastIndexedPosition(string indexName) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => EventTypeIndex.IsEventTypeIndex(indexName);
}
