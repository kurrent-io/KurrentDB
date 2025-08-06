// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

class DefaultIndexReader(
	DuckDbDataSource db,
	DefaultIndexProcessor processor,
	DefaultIndexInFlightRecords inFlightRecords,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(index) {
	protected override int GetId(string streamName) => 0;

	protected override IEnumerable<IndexQueryRecord> GetIndexRecords(int _, TFPos startPosition, int maxCount) {
		var range = db.Pool.Query<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQuery>(new(startPosition.PreparePosition, maxCount));
		if (range.Count < maxCount) {
			var inFlight = inFlightRecords.TryGetInFlightRecords(startPosition, range, maxCount);
			range.AddRange(inFlight);
		}
		return range;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;
	public override bool CanReadIndex(string indexName)=> indexName == DefaultIndex.Name;
}

