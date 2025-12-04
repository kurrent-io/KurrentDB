// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexReader(
	DuckDBConnectionPool sharedPool,
	DefaultIndexProcessor processor,
	IndexInFlightRecords inFlightRecords,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(sharedPool, index) {
	protected override string GetId(string indexName) => string.Empty;

	protected override (List<IndexQueryRecord> Records, bool IsFinal) GetInflightForwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> inFlightRecords.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst);

	protected override List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool db, string id, long startPosition, long endPosition, int maxCount, bool excludeFirst)
		=> excludeFirst
			? db.QueryToList<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQueryExcl>(new(startPosition, endPosition, maxCount))
			: db.QueryToList<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQueryIncl>(new(startPosition, endPosition, maxCount));

	protected override IEnumerable<IndexQueryRecord> GetInflightBackwards(string id, long startPosition, int maxCount, bool excludeFirst)
		=> inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst);

	protected override List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool db, string id, long startPosition, int maxCount, bool excludeFirst)
		=> excludeFirst
			? db.QueryToList<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexBackQueryExcl>(new(startPosition, 0, maxCount))
			: db.QueryToList<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexBackQueryIncl>(new(startPosition, 0, maxCount));

	public override TFPos GetLastIndexedPosition(string indexName) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => indexName == SystemStreams.DefaultSecondaryIndex;
}
