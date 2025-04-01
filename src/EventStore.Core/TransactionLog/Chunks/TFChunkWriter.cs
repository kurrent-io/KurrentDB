// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using Serilog;
using Serilog.Events;

namespace EventStore.Core.TransactionLog.Chunks;

// see CancellationToken comment at the top of TFChunk.cs
public class TFChunkWriter : ITransactionFileWriter {
	public long Position => _writerCheckpoint.ReadNonFlushed();
	public long FlushedPosition => _writerCheckpoint.Read();

	public TFChunk.TFChunk CurrentChunk {
		get { return _currentChunk; }
	}

	public bool NeedsNewChunk => CurrentChunk is
		null or					// new database
		{ IsReadOnly: true };	// database is at a chunk boundary

	private readonly TFChunkDb _db;
	private readonly ICheckpoint _writerCheckpoint;
	private long _nextRecordPosition;
	private bool _inTransaction;

	private TFChunk.TFChunk _currentChunk;

	private const int MaxChunkNumber = TFChunkManager.MaxChunksCount;
	private const int MaxChunkNumberError = MaxChunkNumber - 25_000;
	private const int MaxChunkNumberWarning = MaxChunkNumber - 50_000;

	private static readonly ILogger Log = Serilog.Log.ForContext<TFChunkWriter>();

	public TFChunkWriter(TFChunkDb db) {
		Ensure.NotNull(db, "db");

		_db = db;
		_writerCheckpoint = db.Config.WriterCheckpoint;
		_nextRecordPosition = _writerCheckpoint.Read();
	}

	public async ValueTask Open(CancellationToken token) {
		if (_db.Manager.ChunksCount is 0) {
			// new database
			_currentChunk = null;
		} else if ((_currentChunk = await _db.Manager.TryGetInitializedChunkFor(_nextRecordPosition, token)) is null) {
			// we may have been at a chunk boundary and the new chunk wasn't yet created
			if ((_currentChunk = await _db.Manager.TryGetInitializedChunkFor(_nextRecordPosition - 1, token)) is null)
				throw new Exception($"Failed to get chunk for log position: {_nextRecordPosition}");
		}
	}

	public bool CanWrite(int numBytes) {
		return _currentChunk.ChunkHeader.ChunkEndPosition - _nextRecordPosition >= numBytes;
	}

	public async ValueTask<(bool, long)> Write(ILogRecord record, CancellationToken token) {
		// Workaround: The transaction cannot be canceled in a middle, it should be atomic.
		// There is no way to rollback it on cancellation
		token.ThrowIfCancellationRequested();

		OpenTransaction();

		if (await WriteToTransaction(record, CancellationToken.None) is not { } result) {
			await CompleteChunkInTransaction(CancellationToken.None);
			await AddNewChunk(token: CancellationToken.None);
			CommitTransaction();
			await Flush(token);
			return (false, _nextRecordPosition);
		}

		CommitTransaction();
		return (true, result);
	}

	public void OpenTransaction() {
		if (_inTransaction)
			throw new InvalidOperationException("Attempted to open a new transaction while there was an ongoing transaction.");

		_inTransaction = true;
	}

	public async ValueTask<long?> WriteToTransaction(ILogRecord record, CancellationToken token) {
		return await _currentChunk.TryAppend(record, token) is { Success: true } result
			? _nextRecordPosition = result.NewPosition + _currentChunk.ChunkHeader.ChunkStartPosition
			: null;
	}

	public void CommitTransaction() {
		if (!_inTransaction)
			throw new InvalidOperationException("Attempted to commit a transaction while there was no ongoing transaction.");

		_inTransaction = false;
		_writerCheckpoint.Write(_nextRecordPosition);
	}

	public bool HasOpenTransaction() => _inTransaction;

	public async ValueTask AddNewChunk(ChunkHeader chunkHeader = null,
		ReadOnlyMemory<byte> transformHeader = default, int? chunkSize = null, CancellationToken token = default) {
		var nextChunkNumber = _currentChunk?.ChunkHeader.ChunkEndNumber + 1 ?? 0;
		VerifyChunkNumberLimits(nextChunkNumber);

		_currentChunk = await (chunkHeader is null
			? _db.Manager.AddNewChunk(token)
			: _db.Manager.AddNewChunk(chunkHeader, transformHeader, chunkSize!.Value, token));
	}

	private async ValueTask CompleteChunkInTransaction(CancellationToken token) {
		await _currentChunk.Complete(token);
		_nextRecordPosition = _currentChunk.ChunkHeader.ChunkEndPosition;
	}

	public async ValueTask CompleteChunk(CancellationToken token) {
		// Workaround: The transaction cannot be canceled in a middle, it should be atomic.
		// There is no way to rollback it on cancellation
		token.ThrowIfCancellationRequested();

		OpenTransaction();
		await CompleteChunkInTransaction(CancellationToken.None);
		CommitTransaction();
		await Flush(token);
	}

	private async ValueTask CompleteReplicatedRawChunkInTransaction(TFChunk.TFChunk rawChunk,
		CancellationToken token) {
		await rawChunk.CompleteRaw(token);
		_currentChunk = await _db.Manager.SwitchInTempChunk(rawChunk, verifyHash: true,
			removeChunksWithGreaterNumbers: true, token);

		_nextRecordPosition = rawChunk.ChunkHeader.ChunkEndPosition;
	}

	public async ValueTask CompleteReplicatedRawChunk(TFChunk.TFChunk rawChunk, CancellationToken token) {
		// Workaround: The transaction cannot be canceled in a middle, it should be atomic.
		// There is no way to rollback it on cancellation
		token.ThrowIfCancellationRequested();

		OpenTransaction();
		await CompleteReplicatedRawChunkInTransaction(rawChunk, CancellationToken.None);
		CommitTransaction();
		await Flush(token);
	}

	private static void VerifyChunkNumberLimits(int chunkNumber) {
		switch (chunkNumber)
		{
			case >= MaxChunkNumber:
				throw new Exception($"Max chunk number limit reached: {MaxChunkNumber:N0}. Shutting down.");
			case < MaxChunkNumberWarning:
				break;
			default:
			{
				var level = chunkNumber >= MaxChunkNumberError ? LogEventLevel.Error : LogEventLevel.Warning;
				Log.Write(level, "You are approaching the max chunk number limit: {chunkNumber:N0} / {maxChunkNumber:N0}. " +
				                 "The server will shut down when the limit is reached!", chunkNumber, MaxChunkNumber);
				break;
			}
		}
	}

	public ValueTask DisposeAsync() => Flush(CancellationToken.None);

	public async ValueTask Flush(CancellationToken token) {
		Debug.Assert(HasOpenTransaction() is false);

		if (_currentChunk is null) // the last chunk allocation failed
			return;

		await _currentChunk.Flush(token);
		_writerCheckpoint.Flush();
	}
}
