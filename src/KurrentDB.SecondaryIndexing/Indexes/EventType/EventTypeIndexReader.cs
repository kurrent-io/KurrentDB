// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.Logging;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndexReader(
	DuckDBConnectionPool db,
	DefaultIndexProcessor processor,
	IReadIndex<string> index,
	DefaultIndexInFlightRecords inFlightRecords,
	ILogger<EventTypeIndexReader> log)
	: SecondaryIndexReaderBase(db, index, log) {
	protected override string GetId(string streamName) =>
		EventTypeIndex.TryParseEventType(streamName, out var eventTypeName) ? eventTypeName : string.Empty;

	protected override (List<IndexQueryRecord>, bool) GetInflightForwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst, r => r.EventType == id);

	protected override List<IndexQueryRecord> GetDbRecordsForwards(string id, long startPosition, long endPosition, int maxCount, bool excludeFirst)
		=> excludeFirst
			? Db.QueryToList<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexQueryExcl>(new(id, startPosition, endPosition, maxCount))
			: Db.QueryToList<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexQueryIncl>(new(id, startPosition, endPosition, maxCount));

	protected override IEnumerable<IndexQueryRecord> GetInflightBackwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst, r => r.EventType == id);

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> excludeFirst
			? Db.QueryToList<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexBackQueryExcl>(new(id, startPosition, 0, maxCount))
			: Db.QueryToList<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexBackQueryIncl>(new(id, startPosition, 0, maxCount));

	public override TFPos GetLastIndexedPosition(string indexName) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => EventTypeIndex.IsEventTypeIndex(indexName);
}
