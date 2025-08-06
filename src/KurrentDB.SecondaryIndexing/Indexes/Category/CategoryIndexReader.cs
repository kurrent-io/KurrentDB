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
) : SecondaryIndexReaderBase(index) {
	protected override int GetId(string streamName) =>
		CategoryIndex.TryParseCategoryName(streamName, out var categoryName)
			? processor.GetCategoryId(categoryName)
			: (int)ExpectedVersion.Invalid;

	protected override IEnumerable<IndexQueryRecord> GetIndexRecords(int id, TFPos startPosition, int maxCount) {
		var range = db.Pool.Query<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQuery>(new(id, startPosition.PreparePosition, maxCount));
		if (range.Count < maxCount) {
			// events might be in flight
			var inFlight = inFlightRecords.TryGetInFlightRecords(startPosition, range, maxCount, r => r.CategoryId == id);
			range.AddRange(inFlight);
		}

		return range;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;
	public override bool CanReadIndex(string indexName) => CategoryIndex.IsCategoryIndexStream(indexName);
}
