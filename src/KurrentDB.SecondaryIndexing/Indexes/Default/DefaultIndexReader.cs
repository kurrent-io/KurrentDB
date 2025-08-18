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
	protected override bool TryGetId(string streamName, out int id) {
		id = 0;
		return true;
	}

	protected override List<IndexQueryRecord> GetInFlightRecordsForwards(int _, TFPos startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst).ToList();
	}

	protected override List<IndexQueryRecord> GetDatabaseRecordsForwards(int _, TFPos startPosition, int maxCount, bool excludeFirst) {
		return Db.Pool.Query<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQuery>(
			new(startPosition.PreparePosition + (excludeFirst ? 1 : 0), maxCount));
	}

	protected override List<IndexQueryRecord> GetInFlightRecordsBackwards(int _, TFPos startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst).ToList();
	}

	protected override List<IndexQueryRecord> GetDatabaseRecordsBackwards(int _, TFPos startPosition, int maxCount, bool excludeFirst) {
		return Db.Pool.Query<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexBackQuery>(
			new(startPosition.PreparePosition - (excludeFirst ? 1 : 0), maxCount));
	}

	public override TFPos GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => indexName == SystemStreams.DefaultSecondaryIndex;
}
