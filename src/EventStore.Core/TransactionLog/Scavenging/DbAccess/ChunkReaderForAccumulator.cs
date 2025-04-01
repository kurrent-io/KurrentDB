// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class ChunkReaderForAccumulator<TStreamId> : IChunkReaderForAccumulator<TStreamId> {
	private readonly TFChunkManager _manager;
	private readonly IMetastreamLookup<TStreamId> _metaStreamLookup;
	private readonly IStreamIdConverter<TStreamId> _streamIdConverter;
	private readonly IReadOnlyCheckpoint _replicationChk;
	private readonly int _chunkSize;

	private readonly Func<int, byte[]> _getBuffer;
	private readonly Action _releaseBuffer;

	public ChunkReaderForAccumulator(
		TFChunkManager manager,
		IMetastreamLookup<TStreamId> metastreamLookup,
		IStreamIdConverter<TStreamId> streamIdConverter,
		IReadOnlyCheckpoint replicationChk,
		int chunkSize) {

		_manager = manager;
		_metaStreamLookup = metastreamLookup;
		_streamIdConverter = streamIdConverter;
		_replicationChk = replicationChk;
		_chunkSize = chunkSize;

		var reusableRecordBuffer = new ReusableBuffer(8192);
		_getBuffer = size => reusableRecordBuffer.AcquireAsByteArray(size);
		_releaseBuffer = () => reusableRecordBuffer.Release();
	}

	public async IAsyncEnumerable<AccumulatorRecordType> ReadChunkInto(
		int logicalChunkNumber,
		RecordForAccumulator<TStreamId>.OriginalStreamRecord originalStreamRecord,
		RecordForAccumulator<TStreamId>.MetadataStreamRecord metadataStreamRecord,
		RecordForAccumulator<TStreamId>.TombStoneRecord tombStoneRecord,
		[EnumeratorCancellation] CancellationToken token) {

		// the physical chunk might contain several logical chunks, we are only interested in one of them
		var chunk = await _manager.GetInitializedChunk(logicalChunkNumber, token);
		long chunkStartPos = (long)_chunkSize * logicalChunkNumber;
		long chunkEndPos = (long)_chunkSize * (logicalChunkNumber + 1);
		long nextPos = chunkStartPos;

		var replicationChk = _replicationChk.ReadNonFlushed();

		while (true) {
			if (nextPos >= chunkEndPos) // reached the end of this logical chunk
				break;

			if (nextPos >= replicationChk)
				throw new InvalidOperationException(
					$"Attempt to read at position: {nextPos} which is after the " +
					$"replication checkpoint: {replicationChk}.");

			var localPos = chunk.ChunkHeader.GetLocalLogPosition(nextPos);

			var result = await chunk.TryReadClosestForwardRaw(localPos, _getBuffer, token);

			if (!result.Success) {
				// there is no need to release the reusable buffer here since result.Success is false
				// when attempting to read outside the bounds of a chunk and thus, the buffer will not
				// have been acquired. in other words, whenever the buffer is acquired, either result.Success
				// is true or an exception is thrown.
				break;
			}

			switch (result.RecordType) {
				case LogRecordType.Prepare:
					var prepareView = new PrepareLogRecordView(result.RecordBuffer, result.RecordLength);
					var streamId = _streamIdConverter.ToStreamId(prepareView.EventStreamId);

					if (prepareView.Flags.HasAnyOf(PrepareFlags.StreamDelete)) {
						tombStoneRecord.Reset(
							streamId,
							prepareView.LogPosition,
							prepareView.TimeStamp,
							prepareView.ExpectedVersion + 1);
						yield return AccumulatorRecordType.TombstoneRecord;

					} else if (_metaStreamLookup.IsMetaStream(streamId)) {
						metadataStreamRecord.Reset(
							streamId,
							prepareView.LogPosition,
							prepareView.TimeStamp,
							prepareView.ExpectedVersion + 1,
							StreamMetadata.TryFromJsonBytes(prepareView.Version, prepareView.Data));
						yield return AccumulatorRecordType.MetadataStreamRecord;

					} else {
						originalStreamRecord.Reset(
							streamId,
							prepareView.LogPosition,
							prepareView.TimeStamp);
						yield return AccumulatorRecordType.OriginalStreamRecord;
					}

					break;
				case LogRecordType.Commit:
					break;
				case LogRecordType.System:
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(result.RecordType),
						$"Unexpected log record type: {result.RecordType}");
			}

			nextPos = chunk.ChunkHeader.GetGlobalLogPosition(result.NextPosition);
			_releaseBuffer();
		}
	}
}
