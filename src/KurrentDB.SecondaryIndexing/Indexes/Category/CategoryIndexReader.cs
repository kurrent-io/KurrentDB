// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

// ReSharper disable InvertIf

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

class CategoryIndexReader(
	DuckDbDataSource db,
	CategoryIndexProcessor processor,
	IReadIndex<string> index,
	DefaultIndexInFlightRecords inFlightRecords
) : SecondaryIndexReaderBase(db, index) {
	protected override bool TryGetId(string streamName, out int id) {
		id = (int)ExpectedVersion.Invalid;
		return CategoryIndex.TryParseCategoryName(streamName, out var categoryName)
		       && processor.TryGetCategoryId(categoryName, out id);
	}

	protected override IReadOnlyList<IndexQueryRecord> GetIndexRecordsForwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		var range = excludeFirst
			? Db.Pool.Query<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryExcl>(new(id, startPosition.PreparePosition, maxCount))
			: Db.Pool.Query<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryIncl>(new(id, startPosition.PreparePosition, maxCount));
		if (range.Count < maxCount) {
			// events might be in flight
			var inFlight = inFlightRecords.GetInFlightRecordsForwards(startPosition, range, maxCount, r => r.CategoryId == id);
			range.AddRange(inFlight);
		}

		return range;
	}

	protected override IReadOnlyList<IndexQueryRecord> GetIndexRecordsBackwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		var inFlight = inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, r => r.CategoryId == id).ToList();
		if (inFlight.Count == maxCount) {
			return inFlight;
		}

		var range = excludeFirst
			? Db.Pool.Query<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryExcl>(new(id, startPosition.PreparePosition, maxCount))
			: Db.Pool.Query<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQueryIncl>(new(id, startPosition.PreparePosition, maxCount));

		if (inFlight.Count > 0) {
			range.AddRange(inFlight);
		}

		return range;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => CategoryIndex.IsCategoryIndexStream(indexName);
}
