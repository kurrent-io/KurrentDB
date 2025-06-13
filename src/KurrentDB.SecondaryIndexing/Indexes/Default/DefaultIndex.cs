// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using DotNext;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndex : Disposable, ISecondaryIndex {
	public const string IndexName = $"{SystemStreams.IndexStreamPrefix}all";

	private readonly DuckDbDataSource _db;

	public DefaultIndexProcessor Processor { get; }
	public IReadOnlyList<IVirtualStreamReader> Readers { get; }
	public CategoryIndex CategoryIndex { get; }
	public EventTypeIndex EventTypeIndex { get; }
	public StreamIndex StreamIndex { get; }

	public DefaultIndex(DuckDbDataSource db, IReadIndex<string> readIndex, int commitBatchSize) {
		_db = db;
		_db.InitDb();

		var processor = new DefaultIndexProcessor(db, this, commitBatchSize);
		Processor = processor;

		CategoryIndex = new(db, readIndex, processor.QueryInFlightRecords);
		EventTypeIndex = new(db, readIndex, processor.QueryInFlightRecords);
		StreamIndex = new(db, readIndex);

		Readers = [
			new DefaultIndexReader(db, processor, readIndex),
			CategoryIndex.Reader,
			EventTypeIndex.Reader
		];
	}

	public void Commit() => Processor.Commit();

	public void Index(ResolvedEvent evt) => Processor.Index(evt);

	public long? GetLastSequence() => _db.Pool.QueryFirstOrDefault<long, DefaultSql.GetLastSequenceSql>();

	public long? GetLastPosition() => _db.Pool.QueryFirstOrDefault<long, DefaultSql.GetLastLogPositionSql>();

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Processor.Dispose();
			StreamIndex.Dispose();
		}
		base.Dispose(disposing);
	}
}

delegate IEnumerable<T> QueryInFlightRecords<T>(Func<InFlightRecord, bool> query, Func<InFlightRecord, T> map);

record struct AllRecord(long Seq, long LogPosition);

record struct InFlightRecord(
	long Seq,
	long LogPosition,
	long EventNumber,
	int CategoryId,
	long CategorySeq,
	int EventTypeId,
	long EventTypeSeq
);
