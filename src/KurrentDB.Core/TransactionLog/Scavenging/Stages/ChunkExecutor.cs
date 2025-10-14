// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Scavenging.Data;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace KurrentDB.Core.TransactionLog.Scavenging.Stages;

public class ChunkExecutor<TStreamId, TRecord> : IChunkExecutor<TStreamId> {
	private readonly ILogger _logger;
	private readonly IMetastreamLookup<TStreamId> _metastreamLookup;
	private readonly IChunkRemover<TStreamId, TRecord> _chunkRemover;
	private readonly IChunkManagerForChunkExecutor<TStreamId, TRecord> _chunkManager;
	private readonly long _chunkSize;
	private readonly bool _unsafeIgnoreHardDeletes;
	private readonly int _cancellationCheckPeriod;
	private readonly bool _isArchiver;
	private readonly int _threads;
	private readonly Throttle _throttle;

	public ChunkExecutor(
		ILogger logger,
		IMetastreamLookup<TStreamId> metastreamLookup,
		IChunkRemover<TStreamId, TRecord> chunkRemover,
		IChunkManagerForChunkExecutor<TStreamId, TRecord> chunkManager,
		long chunkSize,
		bool unsafeIgnoreHardDeletes,
		int cancellationCheckPeriod,
		int threads,
		bool isArchiver,
		Throttle throttle) {

		_logger = logger;
		_metastreamLookup = metastreamLookup;
		_chunkRemover = chunkRemover;
		_chunkManager = chunkManager;
		_chunkSize = chunkSize;
		_unsafeIgnoreHardDeletes = unsafeIgnoreHardDeletes;
		_cancellationCheckPeriod = cancellationCheckPeriod;
		_isArchiver = isArchiver;
		_threads = Math.Clamp(threads, TFChunkScavenger.MinThreadCount, TFChunkScavenger.MaxThreadCount);
		_throttle = throttle;

		if (_threads != threads) {
			_logger.Warning("SCAVENGING: Number of threads specified ({SpecifiedThreads}) is out of range. Clamping to {AdjustedThreads}",
				threads, _threads);
		}
	}

	public ValueTask Execute(
		ScavengePoint scavengePoint,
		IScavengeStateForChunkExecutor<TStreamId> state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken) {

		_logger.Debug("SCAVENGING: Started new scavenge chunk execution phase for {scavengePoint}",
			scavengePoint.GetName());

		var checkpoint = new ScavengeCheckpoint.ExecutingChunks(
			scavengePoint: scavengePoint,
			doneLogicalChunkNumber: default);
		state.SetCheckpoint(checkpoint);
		return Execute(checkpoint, state, scavengerLogger, cancellationToken);
	}

	public async ValueTask Execute(
		ScavengeCheckpoint.ExecutingChunks checkpoint,
		IScavengeStateForChunkExecutor<TStreamId> state,
		ITFChunkScavengerLog scavengerLogger,
		CancellationToken cancellationToken) {

		_logger.Debug("SCAVENGING: Executing chunks from checkpoint: {checkpoint}", checkpoint);

		var startFromChunk = checkpoint?.DoneLogicalChunkNumber + 1 ?? 0;
		var scavengePoint = checkpoint.ScavengePoint;

		var physicalChunks = GetAllPhysicalChunks(startFromChunk, scavengePoint, cancellationToken);

		var borrowedStates = new IScavengeStateForChunkExecutorWorker<TStreamId>[_threads];
		var stopwatches = new Stopwatch[_threads];

		for (var i = 0; i < borrowedStates.Length; i++) {
			borrowedStates[i] = state.BorrowStateForWorker();
			stopwatches[i] = new Stopwatch();
		}

		try {
			await ParallelLoop.RunWithTrailingCheckpointAsync(
				source: physicalChunks,
				degreeOfParallelism: _threads,
				getCheckpointInclusive: physicalChunk => physicalChunk.ChunkEndNumber,
				getCheckpointExclusive: physicalChunk => {
					if (physicalChunk.ChunkStartNumber == 0)
						return null;
					return physicalChunk.ChunkStartNumber - 1;
				},
				process: async (slot, physicalChunk, cancellationToken) => {
					// this is called on other threads
					var concurrentState = borrowedStates[slot];
					var sw = stopwatches[slot];

					// the physical chunks do not overlap in chunk range, so we can sum
					// and reset them concurrently
					var physicalWeight = concurrentState.SumChunkWeights(
						physicalChunk.ChunkStartNumber,
						physicalChunk.ChunkEndNumber);

					if (physicalChunk.IsRemote) {
						// Later the archiver node will scavenge remote chunks
						// The other nodes will never scavenge them (because the archiver will)
						_logger.Debug(
							"SCAVENGING: Skipped physical chunk: {oldChunkName} " +
							"with weight {physicalWeight:N0} because it is remote.",
							physicalChunk.Name,
							physicalWeight);

					} else if (await _chunkRemover.StartRemovingIfNotRetained(
						scavengePoint,
						concurrentState,
						physicalChunk,
						cancellationToken)) {

						// the chunk is being removed, but is not necessarily removed yet (it will wait
						// for readers to complete)
						// skip scavenging this chunk, still reset the weights (because there won't be
						// any chunk left to have any weight).
						// the removal was not dependent on the weight, so if the chunk is not removed
						// (e.g. because we shutdown), then that is fine, it will just be removed next scavenge.
						concurrentState.ResetChunkWeights(
							physicalChunk.ChunkStartNumber,
							physicalChunk.ChunkEndNumber);

					} else if (_isArchiver) {
						// Prevent the archive node from executing any chunks, which is important because it
						// does not yet scavenge remote chunks, but we need to keep the remote and local chunks
						// in sync (e.g. with the same weight) so that we do scavenge them later.
						// (otherwise a scavenge with threshold -1 to execute all chunks would be necessary)
						_logger.Debug(
							"SCAVENGING: Skipped physical chunk: {oldChunkName} " +
							"with weight {physicalWeight:N0} because we are the archiver.",
							physicalChunk.Name,
							physicalWeight);

					} else if (physicalWeight > scavengePoint.Threshold || _unsafeIgnoreHardDeletes) {
						await ExecutePhysicalChunk(
							physicalWeight,
							scavengePoint,
							concurrentState,
							scavengerLogger,
							physicalChunk,
							sw,
							cancellationToken);

						// resetting must happen after execution, but need not be in a transaction
						// which is handy, because we cant run transactions concurrently very well
						// https://www.sqlite.org/cgi/src/doc/begin-concurrent/doc/begin_concurrent.md)
						concurrentState.ResetChunkWeights(
							physicalChunk.ChunkStartNumber,
							physicalChunk.ChunkEndNumber);

					} else {
						_logger.Debug(
							"SCAVENGING: Skipped physical chunk: {oldChunkName} " +
							"with weight {physicalWeight:N0}. The threshold is {threshold}.",
							physicalChunk.Name,
							physicalWeight, scavengePoint.Threshold);
					}

					cancellationToken.ThrowIfCancellationRequested();
				},
				emitCheckpoint: chunkEndNumber => {
					// this is called on the thread that called the loop, which does not do any of
					// the processing.
					// it is called after an item has been processed and before the slot is used
					// to process another item. this gives us a meaningful opportunity to rest.
					state.SetCheckpoint(
						new ScavengeCheckpoint.ExecutingChunks(
							scavengePoint,
							chunkEndNumber));

					if (_threads == 1) {
						_throttle.Rest(cancellationToken);
					} else {
						// running a multithreaded scavenge with throttle < 100
						// is rejected by the AdminController.
					}
				},
				token: cancellationToken);
		} finally {
			for (var i = 0; i < borrowedStates.Length; i++) {
				borrowedStates[i].Dispose();
			}
		}
	}

	private async IAsyncEnumerable<IChunkReaderForExecutor<TStreamId, TRecord>> GetAllPhysicalChunks(
		int startFromChunk,
		ScavengePoint scavengePoint,
		[EnumeratorCancellation] CancellationToken token) {

		var scavengePos = _chunkSize * startFromChunk;
		var upTo = scavengePoint.Position;
		while (scavengePos < upTo) {
			// in bounds because we stop before the scavenge point
			var physicalChunk = await _chunkManager.GetChunkReaderFor(scavengePos, token);

			if (!physicalChunk.IsReadOnly)
				throw new Exception(
					$"Reached open chunk before scavenge point. " +
					$"Chunk {physicalChunk.ChunkStartNumber}. ScavengePoint: {upTo}.");

			yield return physicalChunk;

			scavengePos = physicalChunk.ChunkEndPosition;
		}
	}

	private async ValueTask ExecutePhysicalChunk(
		float physicalWeight,
		ScavengePoint scavengePoint,
		IScavengeStateForChunkExecutorWorker<TStreamId> state,
		ITFChunkScavengerLog scavengerLogger,
		IChunkReaderForExecutor<TStreamId, TRecord> sourceChunk,
		Stopwatch sw,
		CancellationToken cancellationToken) {

		sw.Restart();

		int chunkStartNumber = sourceChunk.ChunkStartNumber;
		long chunkStartPos = sourceChunk.ChunkStartPosition;
		int chunkEndNumber = sourceChunk.ChunkEndNumber;
		long chunkEndPos = sourceChunk.ChunkEndPosition;
		var oldChunkName = sourceChunk.Name;

		_logger.Debug(
			"SCAVENGING: Started to scavenge physical chunk: {oldChunkName} " +
			"with weight {physicalWeight:N0}. " +
			"{chunkStartNumber} => {chunkEndNumber} ({chunkStartPosition} => {chunkEndPosition})",
			oldChunkName,
			physicalWeight,
			chunkStartNumber, chunkEndNumber, chunkStartPos, chunkEndPos);

		IChunkWriterForExecutor<TStreamId, TRecord> outputChunk;
		try {
			outputChunk = await _chunkManager.CreateChunkWriter(sourceChunk, cancellationToken);
			_logger.Debug(
				"SCAVENGING: Resulting temp chunk file: {tmpChunkPath}.",
				Path.GetFileName(outputChunk.LocalFileName));

		} catch (IOException ex) {
			_logger.Error(ex,
				"IOException during creating new chunk for scavenging purposes. " +
				"Stopping scavenging process...");
			throw;
		}

		try {
			var cancellationCheckCounter = 0;
			var discardedCount = 0;
			var keptCount = 0;

			// nonPrepareRecord and prepareRecord ae reused through the iteration
			var nonPrepareRecord = new RecordForExecutor<TStreamId, TRecord>.NonPrepare();
			var prepareRecord = new RecordForExecutor<TStreamId, TRecord>.Prepare();

			await foreach (var isPrepare in sourceChunk.ReadInto(nonPrepareRecord, prepareRecord, cancellationToken)) {
				if (isPrepare) {
					if (ShouldDiscard(state, scavengePoint, prepareRecord)) {
						discardedCount++;
					} else {
						keptCount++;
						await outputChunk.WriteRecord(prepareRecord, cancellationToken);
					}
				} else {
					keptCount++;
					await outputChunk.WriteRecord(nonPrepareRecord, cancellationToken);
				}

				if (++cancellationCheckCounter == _cancellationCheckPeriod) {
					cancellationCheckCounter = 0;
					cancellationToken.ThrowIfCancellationRequested();
				}
			}

			_logger.Debug(
				"SCAVENGING: Scavenging {oldChunkName} traversed {recordsCount:N0}. " +
				" Kept {keptCount:N0}. Discarded {discardedCount:N0}",
				oldChunkName, discardedCount + keptCount,
				keptCount, discardedCount);

			var (newFileName, newFileSize) = await outputChunk.Complete(cancellationToken);

			var elapsed = sw.Elapsed;
			_logger.Debug(
				"SCAVENGING: Scavenging of chunks:"
				+ "\n{oldChunkName}"
				+ "\ncompleted in {elapsed}."
				+ "\nNew chunk: {tmpChunkPath} --> #{chunkStartNumber}-{chunkEndNumber} ({newChunk})."
				+ "\nOld chunk total size: {oldSize}, scavenged chunk size: {newSize}.",
				oldChunkName,
				elapsed,
				Path.GetFileName(outputChunk.LocalFileName), chunkStartNumber, chunkEndNumber,
				Path.GetFileName(newFileName),
				sourceChunk.FileSize, newFileSize);

			var spaceSaved = sourceChunk.FileSize - newFileSize;
			scavengerLogger.ChunksScavenged(chunkStartNumber, chunkEndNumber, elapsed, spaceSaved);

		} catch (FileBeingDeletedException exc) {
			_logger.Information(
				"SCAVENGING: Got FileBeingDeletedException exception during scavenging, that probably means some chunks were re-replicated."
				+ "\nStopping scavenging and removing temp chunk '{tmpChunkPath}'..."
				+ "\nException message: {e}.",
				outputChunk.LocalFileName,
				exc.Message);

			outputChunk.Abort(deleteImmediately: true);
			throw;

		} catch (OperationCanceledException) {
			_logger.Information("SCAVENGING: Cancelled at: {oldChunkName}", oldChunkName);
			outputChunk.Abort(deleteImmediately: false);
			throw;

		} catch (Exception ex) {
			_logger.Information(
				ex,
				"SCAVENGING: Got exception while scavenging chunk: #{chunkStartNumber}-{chunkEndNumber}.",
				chunkStartNumber, chunkEndNumber);

			outputChunk.Abort(deleteImmediately: true);
			throw;
		}
	}

	private bool ShouldDiscard(
		IScavengeStateForChunkExecutorWorker<TStreamId> state,
		ScavengePoint scavengePoint,
		RecordForExecutor<TStreamId, TRecord>.Prepare record) {

		// the discard points ought to be sufficient, but sometimes this will be quicker
		// and it is a nice safety net
		if (record.LogPosition >= scavengePoint.Position)
			return false;

		var details = GetStreamExecutionDetails(
			state,
			record.StreamId);

		if (!record.IsSelfCommitted) {
			// deal with transactions first. since it is not self committed, this prepare is
			// associated with an explicit transaction. is one of: begin, data, end.
			if (details.IsTombstoned) {
				// explicit transaction in a tombstoned stream.
				if (_unsafeIgnoreHardDeletes) {
					// remove all prepares including the tombstone
					return true;
				} else {
					// remove all the prepares except
					// - the tombstone itself and
					// - any TransactionBegins (because old scavenge keeps these if there is any
					//   doubt about whether it has been committed)
					if (record.IsTombstone || record.IsTransactionBegin) {
						return false;
					} else {
						return true;
					}
				}
			} else {
				// keep it all.
				// we could discard from transactions sometimes, either by accumulating a state for them
				// or doing a similar trick as old scavenge and limiting it to transactions that were
				// stated and committed in the same chunk. however for now this isn't considered so
				// important because someone with transactions to scavenge has probably scavenged them
				// already with old scavenge. could be added later
				return false;
			}
		}

		if (details.IsTombstoned) {
			if (_unsafeIgnoreHardDeletes) {
				// remove _everything_ for metadata and original streams
				_logger.Information(
					"SCAVENGING: Removing hard deleted stream tombstone for stream {stream} at position {transactionPosition}",
					record.StreamId, record.LogPosition);
				return true;
			}

			if (_metastreamLookup.IsMetaStream(record.StreamId)) {
				// when the original stream is tombstoned we can discard the _whole_ metadata stream
				return true;
			}

			// otherwise obey the discard points below.
		}

		// if discardPoint says discard then discard.
		if (details.DiscardPoint.ShouldDiscard(record.EventNumber)) {
			return true;
		}

		// if maybeDiscardPoint says discard then maybe we can discard - depends on maxage
		if (!details.MaybeDiscardPoint.ShouldDiscard(record.EventNumber)) {
			// both discard points said do not discard, so dont.
			return false;
		}

		// discard said no, but maybe discard said yes
		if (!details.MaxAge.HasValue) {
			return false;
		}

		return record.TimeStamp < scavengePoint.EffectiveNow - details.MaxAge;
	}

	private ChunkExecutionInfo GetStreamExecutionDetails(
		IScavengeStateForChunkExecutorWorker<TStreamId> state,
		TStreamId streamId) {

		if (_metastreamLookup.IsMetaStream(streamId)) {
			if (!state.TryGetMetastreamData(streamId, out var metastreamData)) {
				metastreamData = MetastreamData.Empty;
			}

			return new ChunkExecutionInfo(
				isTombstoned: metastreamData.IsTombstoned,
				discardPoint: metastreamData.DiscardPoint,
				maybeDiscardPoint: DiscardPoint.KeepAll,
				maxAge: null);
		} else {
			// original stream
			if (state.TryGetChunkExecutionInfo(streamId, out var details)) {
				return details;
			} else {
				return new ChunkExecutionInfo(
					isTombstoned: false,
					discardPoint: DiscardPoint.KeepAll,
					maybeDiscardPoint: DiscardPoint.KeepAll,
					maxAge: null);
			}
		}
	}
}
