// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

// ReSharper disable InvertIf

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexReader(
	DuckDbDataSource db,
	CategoryIndexProcessor processor,
	IReadIndex<string> index,
	QueryInFlightRecords<CategoryRecord> queryInFlightRecords
) : SecondaryIndexReaderBase(index) {
	protected override long GetId(string streamName) =>
		CategoryIndex.TryParseCategoryName(streamName, out var categoryName)
			? processor.GetCategoryId(categoryName)
			: ExpectedVersion.Invalid;

	protected override long GetLastIndexedSequence(long id) => processor.GetLastEventNumber((int)id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = db.Pool.Query<CategoryIndexQueryArgs, CategoryRecord, CategoryIndexQuery>(
			new((int)id, fromEventNumber, toEventNumber));
		if (range.Count < toEventNumber - fromEventNumber + 1) {
			// events might be in flight
			var inFlight = queryInFlightRecords(
				r => r.CategoryId == id && r.CategorySeq >= fromEventNumber && r.CategorySeq <= toEventNumber,
				r => new(r.CategorySeq, r.LogPosition)
			);
			range.AddRange(inFlight);
		}

		return range.Select(x => new IndexedPrepare(x.CategorySeq, x.LogPosition));
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadStream(string streamId) => CategoryIndex.IsCategoryIndexStream(streamId);
}
