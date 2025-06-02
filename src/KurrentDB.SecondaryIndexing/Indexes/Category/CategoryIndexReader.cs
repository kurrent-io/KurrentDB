// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategoryIndexConstants;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal class CategoryIndexReader<TStreamId>(
	DuckDbDataSource db,
	CategoryIndexProcessor processor,
	IReadIndex<TStreamId> index
)
	: SecondaryIndexReaderBase<TStreamId>(index) {

	protected override long GetId(string streamName) {
		if (!streamName.StartsWith(IndexPrefix)) {
			return ExpectedVersion.Invalid;
		}

		var categoryName = streamName[8..];
		return processor.GetCategoryId(categoryName);
	}

	protected override long GetLastIndexedSequence(long id) => processor.GetLastEventNumber(id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = db.Pool.Query<CategoryIndexQueryArgs, CategoryRecord, CategoryIndexQuery>(new((int)id, fromEventNumber, toEventNumber));
		return range.Select(x => new IndexedPrepare(x.CategorySeq, x.EventNumber, x.LogPosition));
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastCommittedPosition;

	public override bool CanReadStream(string streamId) => streamId.StartsWith(IndexPrefix);
}
