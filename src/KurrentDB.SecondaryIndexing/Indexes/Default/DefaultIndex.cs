// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using DotNext;
using KurrentDB.Core.Index.Hashes;
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

	public ISecondaryIndexProcessor Processor { get; }
	public CategoryIndex CategoryIndex { get; }
	public EventTypeIndex EventTypeIndex { get; }
	public StreamIndex StreamIndex { get; }

	public DefaultIndex(
		DuckDbDataSource db,
		IReadIndex<string> readIndex,
		ILongHasher<string> hasher,
		DefaultIndexProcessor processor,
		DefaultIndexInFlightRecordsCache inFlightRecordsCache,
		int commitSize
	) : this(db, readIndex, hasher, processor, inFlightRecordsCache,
		new SecondaryIndexingPluginOptions { CommitBatchSize = commitSize }) {
	}

	[UsedImplicitly]
	public DefaultIndex(
		DuckDbDataSource db,
		IReadIndex<string> readIndex,
		ILongHasher<string> hasher,
		DefaultIndexProcessor processor,
		DefaultIndexInFlightRecordsCache inFlightRecordsCache,
		SecondaryIndexingPluginOptions options
	) {
		db.InitDb();

		Processor = processor;

		CategoryIndex = new(db, readIndex, inFlightRecordsCache.QueryInFlightRecords);
		EventTypeIndex = new(db, readIndex, inFlightRecordsCache.QueryInFlightRecords);
		StreamIndex = new(db, readIndex, hasher);
	}

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
