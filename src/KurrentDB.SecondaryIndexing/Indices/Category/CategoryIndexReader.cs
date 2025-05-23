// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indices.DuckDb;
using KurrentDB.SecondaryIndexing.Metrics;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indices.Category.CategorySql;
using static KurrentDB.SecondaryIndexing.Indices.Category.CategoryIndexConstants;

namespace KurrentDB.SecondaryIndexing.Indices.Category;

internal class CategoryIndexReader<TStreamId>(
	DuckDbDataSource db,
	CategoryIndexProcessor<TStreamId> processor,
	IReadIndex<TStreamId> index
)
	: DuckDbIndexReader<TStreamId>(index) {

	protected override long GetId(string streamName) {
		if (!streamName.StartsWith(IndexPrefix)) {
			return ExpectedVersion.Invalid;
		}

		var categoryName = streamName[8..];
		return processor.GetCategoryId(categoryName);
	}

	protected override long GetLastSequence(long id) => processor.GetLastEventNumber(id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber)
		=> GetRecords(id, fromEventNumber, toEventNumber);

	public override long GetLastIndexedPosition(string streamId) =>
		processor.LastCommittedPosition;

	public override bool CanReadStream(string streamId) =>
		streamId.StartsWith(IndexPrefix);

	private IEnumerable<IndexedPrepare> GetRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = QueryCategoryIndex(id, fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.category_seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	private List<CategoryRecord> QueryCategoryIndex(long id, long fromEventNumber, long toEventNumber) {
		using var duration = SecondaryIndexMetrics.MeasureIndex("duck_get_cat_range");
		return db.Pool.Query<(long, long, long), CategoryRecord, QueryCategoryIndexSql>((id, fromEventNumber, toEventNumber));
	}
}
