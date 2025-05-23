// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indices.Default;

public static class DefaultIndexConstants {
	public const string IndexName = $"{SystemStreams.IndexStreamPrefix}all";
}

public class DefaultIndex<TStreamId> : Disposable, ISecondaryIndex {
	private readonly DuckDbDataSource _db;

	public ISecondaryIndexProcessor Processor { get; }

	public IReadOnlyList<IVirtualStreamReader> Readers { get; }

	public DefaultIndex(DuckDbDataSource db, IReadIndex<TStreamId> readIndex) {
		_db = db;
		_db.InitDb();

		var connection = db.OpenNewConnection();

		var processor = new DefaultSecondaryIndexProcessor<TStreamId>(connection, this);
		Processor = processor;
		Readers = [new DefaultIndexReader<TStreamId>(db, processor, readIndex)];
	}

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

public record struct SequenceRecord(long Id, long Sequence);
