// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Transforms;
using EventStore.Core.Transforms.Identity;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Services.Replication.LogReplication;

public abstract class LogReplicationWithExistingDbFixture<TLogFormat, TStreamId> : LogReplicationFixture<TLogFormat, TStreamId> {
	private readonly Random _random = new();
	protected LogFormatAbstractor<TStreamId> LogFormat;
	protected const int DataSize = 3333;

	protected override async Task SetUpDbs(TFChunkDb leaderDb, TFChunkDb replicaDb) {
		await CreateChunks(leaderDb);
	}

	protected abstract Task CreateChunks(TFChunkDb leaderDb);

	protected static async Task CreateChunk(TFChunkDb db, bool raw, bool complete, int chunkStartNumber, int chunkEndNumber, ILogRecord[] logRecords, CancellationToken token = default) {
		var filename = db.Manager.FileSystem.LocalNamingStrategy.GetFilenameFor(chunkStartNumber, raw ? 1 : 0);

		if (raw && !complete)
			throw new InvalidOperationException("A raw chunk must be complete");

		if (!raw && chunkStartNumber != chunkEndNumber)
			throw new InvalidOperationException(
				$"{nameof(chunkStartNumber)} should be equal to {nameof(chunkEndNumber)} for non-raw chunks");

		var header = new ChunkHeader(
			version: TFChunk.CurrentChunkVersion,
			minCompatibleVersion: TFChunk.CurrentChunkVersion,
			chunkSize: ChunkSize,
			chunkStartNumber: chunkStartNumber,
			chunkEndNumber: chunkEndNumber,
			isScavenged: raw,
			chunkId: Guid.NewGuid(),
			transformType: TransformType.Identity);

		var chunk = await TFChunk.CreateWithHeader(
			db.Manager.FileSystem,
			filename: filename,
			header: header,
			fileSize: TFChunk.GetAlignedSize(db.Config.ChunkSize + ChunkHeader.Size + ChunkFooter.Size),
			inMem: false,
			unbuffered: db.Config.Unbuffered,
			writethrough: db.Config.WriteThrough,
			reduceFileCachePressure: db.Config.ReduceFileCachePressure,
			tracker: new TFChunkTracker.NoOp(),
			transformFactory: new IdentityChunkTransformFactory(),
			getTransformFactory: DbTransformManager.Default,
			transformHeader: ReadOnlyMemory<byte>.Empty,
			token);

		var posMaps = new List<PosMap>();

		for (var i = 0; i < logRecords.Length; i++) {
			var logRecord = logRecords[i];
			var logicalPos = chunk.ChunkHeader.GetLocalLogPosition(logRecord.LogPosition);
			var actualPos = chunk.RawWriterPosition - ChunkHeader.Size;
			var writeResult = await chunk.TryAppend(logRecord, token);

			if (!writeResult.Success)
				throw new Exception("Failed to append log record");

			var writerPos = chunk.ChunkHeader.ChunkStartPosition + writeResult.NewPosition;

			// move the (intercepted) writer checkpoint to the expected positions so that they can be compared with the
			// replica's writer checkpoints during tests
			if (!raw &&
			    (logRecord.IsTransactionBoundary() /* complete transaction */
			     || i == logRecords.Length - 1)) /* incomplete transaction at the end of a chunk - commit for backwards compatibility */
				db.Config.WriterCheckpoint.Write(writerPos);

			posMaps.Add(new PosMap(logicalPos, actualPos));
		}

		if (raw)
			await chunk.CompleteScavenge(posMaps, token);
		else if (complete)
			await chunk.Complete(token);
		else
			await chunk.Flush(token);

		if (complete)
			db.Config.WriterCheckpoint.Write(chunk.ChunkHeader.ChunkEndPosition);

		chunk.Dispose();
		chunk.WaitForDestroy(0);
	}

	private ILogRecord CreatePrepare(long logPosition, PrepareFlags flags) {
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		var data = new byte[DataSize];
		_random.NextBytes(data);

		return LogFormat.RecordFactory.CreatePrepare(
			logPosition: logPosition,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			transactionPosition: 0,
			transactionOffset: 0,
			eventStreamId: streamId,
			expectedVersion: 0,
			timeStamp: DateTime.UtcNow,
			flags: flags,
			eventType: eventTypeId,
			data: data,
			metadata: ReadOnlyMemory<byte>.Empty
		);
	}

	protected ILogRecord[] GenerateLogRecords(int chunkNumber, int[] transactionSizes, out long writerPos) {
		var logPosition = chunkNumber * ChunkSize;

		var logRecords = new List<ILogRecord>();
		foreach (var transactionSize in transactionSizes) {
			// a negative transaction size represents an incomplete transaction
			var incomplete = transactionSize < 0;
			var txSize = Math.Abs(transactionSize);

			for (var i = 0; i < txSize; i++) {
				var flags = PrepareFlags.Data | PrepareFlags.IsCommitted;
				if (i == 0) flags |= PrepareFlags.TransactionBegin;
				if (!incomplete && i == txSize - 1) flags |= PrepareFlags.TransactionEnd;
				var logRecord = CreatePrepare(logPosition, flags);
				logPosition += logRecord.GetSizeWithLengthPrefixAndSuffix();
				logRecords.Add(logRecord);
			}
		}

		writerPos = logPosition;

		return logRecords.ToArray();
	}
}
