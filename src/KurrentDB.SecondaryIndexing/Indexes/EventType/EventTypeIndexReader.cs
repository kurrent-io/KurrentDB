// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

class EventTypeIndexReader(
	DuckDbDataSource db,
	EventTypeIndexProcessor processor,
	IReadIndex<string> index,
	DefaultIndexInFlightRecords inFlightRecords
) : SecondaryIndexReaderBase(db, index) {
	protected override bool TryGetId(string streamName, out int id) {
		id = (int)ExpectedVersion.Invalid;
		return EventTypeIndex.TryParseEventType(streamName, out var eventTypeName)
		       && processor.TryGetEventTypeId(eventTypeName, out id);
	}

	protected override List<IndexQueryRecord> GetInFlightRecordsForwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst, r => r.EventTypeId == id).ToList();
	}

	protected override List<IndexQueryRecord> GetDatabaseRecordsForwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return Db.Pool.Query<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexQuery>(
			new(id, startPosition.PreparePosition + (excludeFirst ? 1 : 0), maxCount));
	}

	protected override List<IndexQueryRecord> GetInFlightRecordsBackwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst, r => r.EventTypeId == id).ToList();
	}

	protected override List<IndexQueryRecord> GetDatabaseRecordsBackwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return Db.Pool.Query<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexBackQuery>(
			new(id, startPosition.PreparePosition - (excludeFirst ? 1 : 0), maxCount));
	}

	public override TFPos GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => EventTypeIndex.IsEventTypeIndex(indexName);
}
