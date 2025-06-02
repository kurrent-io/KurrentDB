// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexReader<TStreamId>(
	DuckDbDataSource db,
	DefaultIndexProcessor<TStreamId> processor,
	IReadIndex<TStreamId> index
) : SecondaryIndexReaderBase<TStreamId>(index) {
	protected override long GetId(string streamName) => 0;

	protected override long GetLastIndexedSequence(long id) => processor.LastCommittedSequence;

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long _, long fromEventNumber, long toEventNumber) {
		var range = QueryDefaultIndex(fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.Seq, x.EventNumber, x.LogPosition));
		return indexPrepares;
	}

	public override long GetLastIndexedPosition(string streamId) => processor.LastCommittedPosition;

	public override bool CanReadStream(string streamId) => streamId == DefaultIndexConstants.IndexName;

	private List<AllRecord> QueryDefaultIndex(long fromEventNumber, long toEventNumber) {
		return db.Pool.Query<(long, long), AllRecord, QueryDefaultIndexSql>((fromEventNumber, toEventNumber));
	}

	private struct QueryDefaultIndexSql : IQuery<(long, long), AllRecord> {
		public static BindingContext Bind(in (long, long) args, PreparedStatement statement) => new(statement) {
			args.Item1,
			args.Item2
		};

		public static ReadOnlySpan<byte> CommandText =>
			"select seq, log_position, event_number from idx_all where seq>=$1 and seq<=$2"u8;

		public static AllRecord Parse(ref DataChunk.Row row) => new() {
			Seq = row.ReadInt64(),
			LogPosition = row.ReadInt64(),
			EventNumber = row.ReadInt32(),
		};
	}
}
