// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeIndexConstants;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndexReader<TStreamId>(
	DuckDbDataSource db,
	EventTypeIndexProcessor processor,
	IReadIndex<TStreamId> index
)
	: SecondaryIndexReaderBase<TStreamId>(index) {

	protected override long GetId(string streamName) {
		if (!streamName.StartsWith(IndexPrefix)) {
			return ExpectedVersion.Invalid;
		}

		var eventTypeName = streamName[8..];
		return processor.GetEventTypeId(eventTypeName);
	}

	protected override long GetLastIndexedSequence(long id) => processor.GetLastEventNumber(id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = db.Pool.Query<ReadEventTypeIndexQueryArgs, EventTypeRecord, ReadEventTypeIndexQuery>(new((int)id, fromEventNumber, toEventNumber));
		var indexPrepares = range.Select(x => new IndexedPrepare(x.EventTypeSeq, x.EventNumber, x.LogPosition));
		return indexPrepares;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastCommittedPosition;

	public override bool CanReadStream(string streamId) => streamId.StartsWith(IndexPrefix);

}
