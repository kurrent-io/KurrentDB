// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

// ReSharper disable InvertIf

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexReader(
	DuckDBConnectionPool db,
	DefaultIndexProcessor processor,
	IReadIndex<string> index,
	DefaultIndexInFlightRecords inFlightRecords
) : SecondaryIndexReaderBase(db, index) {
	protected override string GetId(string indexName) =>
		CategoryIndex.TryParseCategoryName(indexName, out var categoryName)
			? categoryName
			: string.Empty;

	protected override IReadOnlyList<IndexQueryRecord> GetIndexRecordsForwards(string id, TFPos startPosition, int maxCount, bool excludeFirst) {
		var range = excludeFirst
			? Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryExcl>(new(id, startPosition.PreparePosition, maxCount))
			: Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryIncl>(new(id, startPosition.PreparePosition, maxCount));
		if (range.Count < maxCount) {
			// events might be in flight
			var inFlight = inFlightRecords.GetInFlightRecordsForwards(startPosition, range, maxCount, r => r.Category == id);
			range.AddRange(inFlight);
		}

		return range;
	}

	protected override IReadOnlyList<IndexQueryRecord> GetIndexRecordsBackwards(string id, TFPos startPosition, int maxCount, bool excludeFirst) {
		var inFlight = inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, r => r.Category == id).ToList();
		if (inFlight.Count == maxCount) {
			return inFlight;
		}

		var range = excludeFirst
			? Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryExcl>(new(id, startPosition.PreparePosition, maxCount))
			: Db.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryIncl>(new(id, startPosition.PreparePosition, maxCount));

		if (inFlight.Count > 0) {
			range.AddRange(inFlight);
		}

		return range;
	}

	public override long GetLastIndexedPosition(string indexName) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => CategoryIndex.IsCategoryIndex(indexName);
}
