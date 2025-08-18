// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

class DefaultIndexReader(
	DuckDbDataSource db,
	DefaultIndexProcessor processor,
	DefaultIndexInFlightRecords inFlightRecords,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(db, index) {
	protected override int GetId(string streamName) => 0;

	protected override IReadOnlyList<IndexQueryRecord> GetIndexRecordsForwards(int _, TFPos startPosition, int maxCount, bool excludeFirst) {
		var range = excludeFirst
			? Db.Pool.Query<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQueryExcl>(new(startPosition.PreparePosition, maxCount))
			: Db.Pool.Query<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQueryIncl>(new(startPosition.PreparePosition, maxCount));
		// ReSharper disable once InvertIf
		if (range.Count < maxCount) {
			var inFlight = inFlightRecords.GetInFlightRecordsForwards(startPosition, range, maxCount);
			range.AddRange(inFlight);
		}

		return range;
	}

	protected override IReadOnlyList<IndexQueryRecord> GetIndexRecordsBackwards(int _, TFPos startPosition, int maxCount, bool excludeFirst) {
		var inFlight = inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount).ToList();
		if (inFlight.Count == maxCount) {
			return inFlight;
		}

		var range = excludeFirst
			? Db.Pool.Query<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexBackQueryExcl>(new(startPosition.PreparePosition, maxCount))
			: Db.Pool.Query<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexBackQueryIncl>(new(startPosition.PreparePosition, maxCount));

		if (inFlight.Count > 0) {
			range.AddRange(inFlight);
		}

		return range;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => indexName == SystemStreams.DefaultSecondaryIndex;
}
