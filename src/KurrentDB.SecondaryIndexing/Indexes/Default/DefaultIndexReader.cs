// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexReader(
	DuckDbDataSource db,
	DefaultIndexProcessor processor,
	DefaultIndexInFlightRecordsCache inFlightRecordsCache,
	IReadIndex<string> index
) : SecondaryIndexReaderBase(index) {
	protected override long GetId(string streamName) => 0;

	protected override long GetLastIndexedSequence(long id) => processor.LastSequence;

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long _, long fromEventNumber, long toEventNumber) {
		var range = db.Pool.Query<(long, long), AllRecord, DefaultSql.DefaultIndexQuery>((fromEventNumber, toEventNumber));
		if (range.Count < toEventNumber - fromEventNumber + 1) {
			var inFlight = inFlightRecordsCache.TryGetInFlightRecords(fromEventNumber, toEventNumber);
			range.AddRange(inFlight);
		}
		var indexPrepares = range.Select(x => new IndexedPrepare(x.Seq, x.LogPosition));
		return indexPrepares;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastIndexedPosition;

	public override bool CanReadStream(string streamId) => streamId == DefaultIndex.IndexName;
}

