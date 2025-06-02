// Copyright (c) Kurrent, Inc and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal static class DefaultIndexConstants {
	public const string IndexName = $"{SystemStreams.IndexStreamPrefix}all";
}

internal class DefaultIndex<TStreamId> : Disposable, ISecondaryIndexExt {
	private readonly DuckDbDataSource _db;

	public DefaultIndexProcessor<TStreamId> Processor { get; }

	public IReadOnlyList<IVirtualStreamReader> Readers { get; }


	public CategoryIndex<TStreamId> CategoryIndex { get; }

	public EventTypeIndex<TStreamId> EventTypeIndex { get; set; }

	public StreamIndex<TStreamId> StreamIndex { get; set; }

	public DefaultIndex(DuckDbDataSource db, IReadIndex<TStreamId> readIndex) {
		_db = db;
		_db.InitDb();

		CategoryIndex = new(db, readIndex);
		EventTypeIndex = new(db, readIndex);
		StreamIndex = new(db, readIndex);

		var processor = new DefaultIndexProcessor<TStreamId>(db, this);
		Processor = processor;
		Readers = [
			new DefaultIndexReader<TStreamId>(db, processor, readIndex),
			..CategoryIndex.Readers,
			..EventTypeIndex.Readers
		];
	}

	public void Commit() => Processor.Commit();

	public void Index(ResolvedEvent evt) => Processor.Index(evt);

	public void Init() {
	}

	public ulong? GetLastSequence() =>
		_db.Pool.QueryFirstOrDefault<ulong, GetLastSequenceSql>();

	public ulong? GetLastPosition() =>
		_db.Pool.QueryFirstOrDefault<ulong, GetLastLogPositionSql>();

	private struct GetLastSequenceSql : IQuery<ulong> {
		public static ReadOnlySpan<byte> CommandText => "select max(seq) from idx_all"u8;

		public static ulong Parse(ref DataChunk.Row row) => row.ReadUInt64();
	}

	private struct GetLastLogPositionSql : IQuery<ulong> {
		public static ReadOnlySpan<byte> CommandText => "select max(log_position) from idx_all"u8;

		public static ulong Parse(ref DataChunk.Row row) => row.ReadUInt64();
	}
}
