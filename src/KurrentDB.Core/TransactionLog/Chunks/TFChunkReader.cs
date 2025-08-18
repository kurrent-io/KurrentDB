// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.TransactionLog.Checkpoint;
using Serilog;

namespace KurrentDB.Core.TransactionLog.Chunks;

public sealed class TFChunkReader : ITransactionFileReader {
	internal static long CachedReads;
	internal static long NotCachedReads;

	private const int MaxRetries = 20;

	private readonly TFChunkDb _db;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly ILogger _log = Log.ForContext<TFChunkReader>();

	public TFChunkReader(TFChunkDb db, IReadOnlyCheckpoint writerCheckpoint) {
		Ensure.NotNull(db, "dbConfig");
		Ensure.NotNull(writerCheckpoint, "writerCheckpoint");

		_db = db;
		_writerCheckpoint = writerCheckpoint;
	}

	public ValueTask<SeqReadResult> TryReadNext<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor
		=> TryReadNextInternal(cursor, 0, token);

	private async ValueTask<SeqReadResult> TryReadNextInternal<TCursor>(TCursor cursor, int retries,
		CancellationToken token)
		where TCursor : IReadCursor {
		while (true) {
			var writerChk = _writerCheckpoint.Read();
			if (cursor.Position >= writerChk)
				return SeqReadResult.Failure;

			var chunk = await _db.Manager.GetInitializedChunkFor(cursor.Position, token);
			RecordReadResult result;
			try {
				result = await chunk.TryReadClosestForward(chunk.ChunkHeader.GetLocalLogPosition(cursor.Position), token);
				CountRead(chunk.IsCached);
			} catch (FileBeingDeletedException) {
				if (retries > MaxRetries)
					throw new Exception(
						string.Format(
							"Got a file that was being deleted {0} times from TFChunkDb, likely a bug there.",
							MaxRetries));
				retries++;
				continue;
			}

			if (result.Success) {
				cursor.Position = chunk.ChunkHeader.ChunkStartPosition + result.NextPosition;
				var postPos =
					result.LogRecord.GetNextLogPosition(result.LogRecord.LogPosition, result.RecordLength);
				var eof = postPos == writerChk;
				return new SeqReadResult(
					true, eof, result.LogRecord, result.RecordLength, result.LogRecord.LogPosition, postPos);
			}

			// we are the end of chunk
			cursor.Position = chunk.ChunkHeader.ChunkEndPosition; // the start of next physical chunk
		}
	}

	public ValueTask<SeqReadResult> TryReadPrev<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor
		=> TryReadPrevInternal(cursor, 0, token);

	private async ValueTask<SeqReadResult> TryReadPrevInternal<TCursor>(TCursor cursor, int retries,
		CancellationToken token)
		where TCursor : IReadCursor {
		for (;; token.ThrowIfCancellationRequested()) {
			var writerChk = _writerCheckpoint.Read();
			// we allow == writerChk, that means read the very last record
			if (cursor.Position > writerChk)
				throw new Exception(string.Format(
					"Requested position {0} is greater than writer checkpoint {1} when requesting to read previous record from TF.",
					cursor.Position, writerChk));
			if (cursor.Position <= 0)
				return SeqReadResult.Failure;

			bool readLast = false;

			if (await _db.Manager.TryGetInitializedChunkFor(cursor.Position, token) is not { } chunk ||
			    cursor.Position == chunk.ChunkHeader.ChunkStartPosition) {
				// we are exactly at the boundary of physical chunks
				// so we switch to previous chunk and request TryReadLast
				readLast = true;
				chunk = await _db.Manager.GetInitializedChunkFor(cursor.Position - 1, token);
			}

			RecordReadResult result;
			try {
				result = await (readLast
					? chunk.TryReadLast(token)
					: chunk.TryReadClosestBackward(chunk.ChunkHeader.GetLocalLogPosition(cursor.Position), token));
				CountRead(chunk.IsCached);
			} catch (FileBeingDeletedException) {
				if (retries > MaxRetries)
					throw new Exception(string.Format(
						"Got a file that was being deleted {0} times from TFChunkDb, likely a bug there.",
						MaxRetries));

				retries++;
				continue;
			}

			if (result.Success) {
				cursor.Position = chunk.ChunkHeader.ChunkStartPosition + result.NextPosition;
				var postPos =
					result.LogRecord.GetNextLogPosition(result.LogRecord.LogPosition, result.RecordLength);
				var eof = postPos == writerChk;
				var res = new SeqReadResult(true, eof, result.LogRecord, result.RecordLength,
					result.LogRecord.LogPosition, postPos);
				return res;
			}

			// we are the beginning of chunk, so need to switch to previous one
			// to do that we set cur position to the exact boundary position between current and previous chunk,
			// this will be handled correctly on next iteration
			cursor.Position = chunk.ChunkHeader.ChunkStartPosition;
		}
	}

	public ValueTask<RecordReadResult> TryReadAt(long position, bool couldBeScavenged, CancellationToken token)
		=> TryReadAtInternal(position, couldBeScavenged, 0, token);

	private async ValueTask<RecordReadResult> TryReadAtInternal(long position, bool couldBeScavenged, int retries, CancellationToken token) {
		while (retries <= MaxRetries) {
			var writerChk = _writerCheckpoint.Read();
			if (position >= writerChk) {
				_log.Warning(
					"Attempted to read at position {position}, which is further than writer checkpoint {writerChk}",
					position, writerChk);
				return RecordReadResult.Failure;
			}

			var chunk = await _db.Manager.GetInitializedChunkFor(position, token);
			try {
				CountRead(chunk.IsCached);
				return await chunk.TryReadAt(chunk.ChunkHeader.GetLocalLogPosition(position), couldBeScavenged, token);
			} catch (FileBeingDeletedException) {
				retries++;
			}
		}

		throw new FileBeingDeletedException(
			"Been told the file was deleted > MaxRetries times. Probably a problem in db.");
	}

	public ValueTask<bool> ExistsAt(long position, CancellationToken token)
		=> ExistsAtInternal(position, 0, token);

	private async ValueTask<bool> ExistsAtInternal(long position, int retries, CancellationToken token) {
		while (retries <= MaxRetries) {
			var writerChk = _writerCheckpoint.Read();
			if (position >= writerChk)
				return false;

			var chunk = await _db.Manager.GetInitializedChunkFor(position, token);
			try {
				CountRead(chunk.IsCached);
				return await chunk.ExistsAt(chunk.ChunkHeader.GetLocalLogPosition(position), token);
			} catch (FileBeingDeletedException) {
				retries++;
			}
		}

		throw new FileBeingDeletedException(
			"Been told the file was deleted > MaxRetries times. Probably a problem in db.");
	}

	private static void CountRead(bool isCached) {
		if (isCached)
			Interlocked.Increment(ref CachedReads);
		else
			Interlocked.Increment(ref NotCachedReads);
	}
}
