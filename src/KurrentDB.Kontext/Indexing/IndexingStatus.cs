// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Indexing;

/// <summary>
/// Per-workspace progress tracker for the FTS and embedding subscriptions.
/// </summary>
public class IndexingStatus {
	ulong _ftsPosition;
	ulong _embeddingPosition;
	long _ftsLastIndexedTimestampTicks = long.MinValue;
	long _embeddingLastIndexedTimestampTicks = long.MinValue;
	long _ftsLastCaughtUpTicks = long.MinValue;
	long _embeddingLastCaughtUpTicks = long.MinValue;

	public ulong FtsPosition => Volatile.Read(ref _ftsPosition);
	public ulong EmbeddingPosition => Volatile.Read(ref _embeddingPosition);

	public DateTime? FtsLastIndexedAt => ReadTimestamp(ref _ftsLastIndexedTimestampTicks);
	public DateTime? EmbeddingLastIndexedAt => ReadTimestamp(ref _embeddingLastIndexedTimestampTicks);

	public DateTime? FtsLastCaughtUpAt => ReadTimestamp(ref _ftsLastCaughtUpTicks);
	public DateTime? EmbeddingLastCaughtUpAt => ReadTimestamp(ref _embeddingLastCaughtUpTicks);

	public void UpdateFtsPosition(ulong commit) => Volatile.Write(ref _ftsPosition, commit);
	public void UpdateEmbeddingPosition(ulong commit) => Volatile.Write(ref _embeddingPosition, commit);

	public void RecordFtsIndexed(DateTime eventTimestamp) =>
		Interlocked.Exchange(ref _ftsLastIndexedTimestampTicks, eventTimestamp.Ticks);

	public void RecordEmbeddingIndexed(DateTime eventTimestamp) =>
		Interlocked.Exchange(ref _embeddingLastIndexedTimestampTicks, eventTimestamp.Ticks);

	public void RecordFtsCaughtUp() => Interlocked.Exchange(ref _ftsLastCaughtUpTicks, DateTime.UtcNow.Ticks);
	public void RecordEmbeddingCaughtUp() => Interlocked.Exchange(ref _embeddingLastCaughtUpTicks, DateTime.UtcNow.Ticks);

	static DateTime? ReadTimestamp(ref long ticks) {
		var v = Interlocked.Read(ref ticks);
		return v == long.MinValue ? null : new DateTime(v, DateTimeKind.Utc);
	}
}
