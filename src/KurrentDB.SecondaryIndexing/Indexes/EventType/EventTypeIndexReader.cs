// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndexReader(
	// DuckDbDataSource db,
	EventTypeIndexProcessor processor,
	IReadIndex<string> index
	// QueryInFlightRecords<EventTypeRecord> queryInFlightRecords
)
	: SecondaryIndexReaderBase(index) {
	protected override long GetId(string streamName) {
		if (!streamName.StartsWith(EventTypeIndex.IndexPrefix)) {
			return ExpectedVersion.Invalid;
		}

		var eventTypeName = streamName[8..];
		return processor.GetEventTypeId(eventTypeName);
	}

	protected override long GetLastIndexedSequence(long id) => processor.GetLastEventNumber(id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber) {
		// var range = db.Pool.Query<ReadEventTypeIndexQueryArgs, EventTypeRecord, ReadEventTypeIndexQuery>(new((int)id, fromEventNumber, toEventNumber));
		// if (range.Count < toEventNumber - fromEventNumber + 1) {
		// 	// events might be in flight
		// 	var inFlight = queryInFlightRecords(
		// 		r => r.EventTypeId == id && r.EventTypeSeq >= fromEventNumber && r.EventTypeSeq <= toEventNumber,
		// 		r => new(r.EventTypeSeq, r.LogPosition, r.EventNumber)
		// 	);
		// 	range.AddRange(inFlight);
		// }
		//
		// var indexPrepares = range.Select(x => new IndexedPrepare(x.EventTypeSeq, x.EventNumber, x.LogPosition));
		// return indexPrepares;
		return [];
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadStream(string streamId) => streamId.StartsWith(EventTypeIndex.IndexPrefix);
}
