// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indices.DuckDb;
using KurrentDB.SecondaryIndexing.Metrics;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indices.Default;

internal class DefaultIndexReader<TStreamId>(
	DuckDbDataSource db,
	DefaultSecondaryIndexProcessor<TStreamId> processor,
	IReadIndex<TStreamId> index
) : DuckDbIndexReader<TStreamId>(index) {
	protected override long GetId(string streamName) => 0;

	protected override long GetLastSequence(long id) => processor.LastSequence - 1;

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long _, long fromEventNumber, long toEventNumber) {
		var range = QueryDefaultIndex(fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastCommittedPosition;

	public override bool CanReadStream(string streamId) => streamId == DefaultIndexConstants.IndexName;

	private List<AllRecord> QueryDefaultIndex(long fromEventNumber, long toEventNumber) {
		using var duration = SecondaryIndexMetrics.MeasureIndex("duck_get_all_range");
		return db.Pool.Query<(long, long), AllRecord, QueryDefaultIndexSql>((fromEventNumber, toEventNumber));
	}

	private struct QueryDefaultIndexSql : IQuery<(long, long), AllRecord> {
		public static BindingContext Bind(in (long, long) args, PreparedStatement statement) => new(statement) {
			args.Item1,
			args.Item2
		};

		public static ReadOnlySpan<byte> CommandText =>
			"select seq, log_position, event_number from idx_all where seq>=1 and seq<=$2"u8;

		public static AllRecord Parse(ref DataChunk.Row row) => new() {
			seq = row.ReadInt64(),
			log_position = row.ReadInt64(),
			event_number = row.ReadInt32(),
		};
	}
}
