// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndex {
	public const string IndexPrefix = $"{SystemStreams.IndexStreamPrefix}et-";

	public EventTypeIndex(
		DuckDbDataSource db,
		IReadIndex<string> readIndex,
		QueryInFlightRecords<EventTypeSql.EventTypeRecord> queryInFlightRecords
	) {
		Processor = new(db);
		Reader = new EventTypeIndexReader(db, Processor, readIndex, queryInFlightRecords);
	}

	public long? GetLastPosition() => Processor.LastIndexedPosition;

	public EventTypeIndexProcessor Processor { get; }

	public IVirtualStreamReader Reader { get; }
}
