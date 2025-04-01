// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.Checkpoint;
using Serilog;

namespace EventStore.Core.TransactionLog.Chunks;

public class TFChunkReader : ITransactionFileReader {
	internal static long CachedReads;
	internal static long NotCachedReads;

	public const int MaxRetries = 20;

	public long CurrentPosition {
		get { return _curPos; }
	}

	private readonly TFChunkDb _db;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private long _curPos;
	private readonly ILogger _log = Log.ForContext<TFChunkReader>();

	public TFChunkReader(TFChunkDb db, IReadOnlyCheckpoint writerCheckpoint, long initialPosition = 0) {
		Ensure.NotNull(db, "dbConfig");
		Ensure.NotNull(writerCheckpoint, "writerCheckpoint");
		Ensure.Nonnegative(initialPosition, "initialPosition");

		_db = db;
		_writerCheckpoint = writerCheckpoint;
		_curPos = initialPosition;
	}

	public void Reposition(long position) {
		_curPos = position;
	}

	public ValueTask<SeqReadResult> TryReadNext(CancellationToken token)
		=> TryReadNextInternal(0, token);

	private async ValueTask<SeqReadResult> TryReadNextInternal(int retries, CancellationToken token) {
		while (true) {
			var pos = _curPos;
			var writerChk = _writerCheckpoint.Read();
			if (pos >= writerChk)
				return SeqReadResult.Failure;

			var chunk = await _db.Manager.GetInitializedChunkFor(pos, token);
			RecordReadResult result;
			try {
				result = await chunk.TryReadClosestForward(chunk.ChunkHeader.GetLocalLogPosition(pos), token);
				CountRead(chunk.IsCached);
			} catch (FileBeingDeletedException) {
				if (retries > MaxRetries)
					throw new Exception(
						string.Format(
							"Got a file that was being deleted {0} times from TFChunkDb, likely a bug there.",
							MaxRetries));
				return await TryReadNextInternal(retries + 1, token);
			}

			if (result.Success) {
				_curPos = chunk.ChunkHeader.ChunkStartPosition + result.NextPosition;
				var postPos =
					result.LogRecord.GetNextLogPosition(result.LogRecord.LogPosition, result.RecordLength);
				var eof = postPos == writerChk;
				return new SeqReadResult(
					true, eof, result.LogRecord, result.RecordLength, result.LogRecord.LogPosition, postPos);
			}

			// we are the end of chunk
			_curPos = chunk.ChunkHeader.ChunkEndPosition; // the start of next physical chunk
		}
	}

	public ValueTask<SeqReadResult> TryReadPrev(CancellationToken token) {
		return TryReadPrevInternal(0, token);
	}

	private async ValueTask<SeqReadResult> TryReadPrevInternal(int retries, CancellationToken token) {
		for (;;token.ThrowIfCancellationRequested()) {
			var pos = _curPos;
			var writerChk = _writerCheckpoint.Read();
			// we allow == writerChk, that means read the very last record
			if (pos > writerChk)
				throw new Exception(string.Format(
					"Requested position {0} is greater than writer checkpoint {1} when requesting to read previous record from TF.",
					pos, writerChk));
			if (pos <= 0)
				return SeqReadResult.Failure;

			bool readLast = false;

			if (await _db.Manager.TryGetInitializedChunkFor(pos, token) is not { } chunk ||
			    pos == chunk.ChunkHeader.ChunkStartPosition) {
				// we are exactly at the boundary of physical chunks
				// so we switch to previous chunk and request TryReadLast
				readLast = true;
				chunk = await _db.Manager.GetInitializedChunkFor(pos - 1, token);
			}

			RecordReadResult result;
			try {
				result = await (readLast
					? chunk.TryReadLast(token)
					: chunk.TryReadClosestBackward(chunk.ChunkHeader.GetLocalLogPosition(pos), token));
				CountRead(chunk.IsCached);
			} catch (FileBeingDeletedException) {
				if (retries > MaxRetries)
					throw new Exception(string.Format(
						"Got a file that was being deleted {0} times from TFChunkDb, likely a bug there.",
						MaxRetries));
				return await TryReadPrevInternal(retries + 1, token);
			}

			if (result.Success) {
				_curPos = chunk.ChunkHeader.ChunkStartPosition + result.NextPosition;
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
			_curPos = chunk.ChunkHeader.ChunkStartPosition;
		}
	}

	public ValueTask<RecordReadResult> TryReadAt(long position, bool couldBeScavenged, CancellationToken token)
		=> TryReadAtInternal(position, couldBeScavenged, 0, token);

	private async ValueTask<RecordReadResult> TryReadAtInternal(long position, bool couldBeScavenged, int retries, CancellationToken token) {
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
			if (retries > MaxRetries)
				throw new FileBeingDeletedException(
					"Been told the file was deleted > MaxRetries times. Probably a problem in db.");
			return await TryReadAtInternal(position, couldBeScavenged, retries + 1, token);
		}
	}

	public ValueTask<bool> ExistsAt(long position, CancellationToken token)
		=> ExistsAtInternal(position, 0, token);

	private async ValueTask<bool> ExistsAtInternal(long position, int retries, CancellationToken token) {
		var writerChk = _writerCheckpoint.Read();
		if (position >= writerChk)
			return false;

		var chunk = await _db.Manager.GetInitializedChunkFor(position, token);
		try {
			CountRead(chunk.IsCached);
			return await chunk.ExistsAt(chunk.ChunkHeader.GetLocalLogPosition(position), token);
		} catch (FileBeingDeletedException) {
			if (retries > MaxRetries)
				throw new FileBeingDeletedException(
					"Been told the file was deleted > MaxRetries times. Probably a problem in db.");
			return await ExistsAtInternal(position, retries + 1, token);
		}
	}

	private static void CountRead(bool isCached) {
		if (isCached)
			Interlocked.Increment(ref CachedReads);
		else
			Interlocked.Increment(ref NotCachedReads);
	}
}
