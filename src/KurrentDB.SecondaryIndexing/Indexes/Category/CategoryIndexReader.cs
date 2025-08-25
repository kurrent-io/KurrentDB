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

	protected override List<IndexQueryRecord> GetInFlightRecordsForwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords
			.GetInFlightRecordsForwards(startPosition, maxCount, excludeFirst, r => r.CategoryId == id).ToList();
	}

	protected override List<IndexQueryRecord> GetDatabaseRecordsForwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return Db.Pool.Query<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQuery>(
			new(id, startPosition.PreparePosition + (excludeFirst ? 1 : 0), maxCount));
	}

	protected override List<IndexQueryRecord> GetInFlightRecordsBackwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return inFlightRecords.GetInFlightRecordsBackwards(startPosition, maxCount, excludeFirst, r => r.CategoryId == id).ToList();
	}

	protected override List<IndexQueryRecord> GetDatabaseRecordsBackwards(int id, TFPos startPosition, int maxCount, bool excludeFirst) {
		return Db.Pool.Query<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexBackQuery>(
			new(id, startPosition.PreparePosition - (excludeFirst ? 1 : 0), maxCount));
	}

	public override TFPos GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadIndex(string indexName) => CategoryIndex.IsCategoryIndexStream(indexName);
}
