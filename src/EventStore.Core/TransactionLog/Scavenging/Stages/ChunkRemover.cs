// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging.Stages;

// responsible for removing chunks that are present in the archive and no longer
// needed locally according to the retention policy
public class ChunkRemover<TStreamId, TRecord> : IChunkRemover<TStreamId, TRecord> {
	private readonly ILogger _logger;
	private readonly AdvancingCheckpoint _archiveCheckpoint;
	private readonly IChunkManagerForChunkRemover _chunkManager;
	private readonly ILocatorCodec _locatorCodec;
	private readonly TimeSpan _retainPeriod;
	private readonly long _retainBytes;

	public ChunkRemover(
		ILogger logger,
		AdvancingCheckpoint archiveCheckpoint,
		IChunkManagerForChunkRemover chunkManager,
		ILocatorCodec locatorCodec,
		TimeSpan retainPeriod,
		long retainBytes) {

		_logger = logger;
		_archiveCheckpoint = archiveCheckpoint;
		_chunkManager = chunkManager;
		_locatorCodec = locatorCodec;
		_retainPeriod = retainPeriod;
		_retainBytes = retainBytes;

		_logger.Debug("SCAVENGING: Chunk retention criteria is Days: {Days}, LogicalBytes: {LogicalBytes}",
			retainPeriod.Days,
			retainBytes);
	}

	// returns true iff removing
	public async ValueTask<bool> StartRemovingIfNotRetained(
		ScavengePoint scavengePoint,
		IScavengeStateForChunkExecutorWorker<TStreamId> concurrentState,
		IChunkReaderForExecutor<TStreamId, TRecord> physicalChunk,
		CancellationToken ct) {

		if (physicalChunk.IsRemote) {
			return false;
		}

		if (!ShouldRemoveForBytes(scavengePoint, physicalChunk)) {
			_logger.Debug(
				"SCAVENGING: ChunkRemover is keeping chunk {PhysicalChunk} because of " +
				"the retention policy for bytes", physicalChunk.Name);
			return false;
		}

		if (!ShouldRemoveForPeriod(scavengePoint, concurrentState, physicalChunk)) {
			_logger.Debug(
				"SCAVENGING: ChunkRemover is keeping chunk {PhysicalChunk} because of " +
				"the retention policy for days", physicalChunk.Name);
			return false;
		}

		if (!await IsConfirmedPresentInArchive(physicalChunk, ct)) {
			_logger.Debug(
				"SCAVENGING: ChunkRemover is keeping chunk {PhysicalChunk} because it " +
				"is not confirmed present in the archive", physicalChunk.Name);
			return false;
		}

		await SwitchOutPhysicalChunk(physicalChunk, ct);
		return true;
	}

	// if the chunk isn't present in the archive there might be a problem with the archiver node
	// however we don't want this to prevent us from being able to scavenge this node, so we keep
	// this chunk rather than terminate the scavenge
	private async ValueTask<bool> IsConfirmedPresentInArchive(
		IChunkReaderForExecutor<TStreamId, TRecord> physicalChunk,
		CancellationToken ct) {

		var logicalChunkNumber = physicalChunk.ChunkEndNumber;

		try {
			var isPresent = await _archiveCheckpoint.IsGreaterThanOrEqualTo(physicalChunk.ChunkEndPosition, ct);
			if (!isPresent) {
				_logger.Warning(
					"Logical chunk {LogicalChunkNumber} is not yet present in the archive",
					logicalChunkNumber);
			}
			return isPresent;
		} catch (Exception ex) when (ex is not OperationCanceledException) {
			_logger.Warning(ex,
				"Unable to determine existence of logical chunk {LogicalChunkNumber} in the archive.",
				logicalChunkNumber);
		}

		return false;
	}

	private bool ShouldRemoveForBytes(
		ScavengePoint scavengePoint,
		IChunkReaderForExecutor<TStreamId, TRecord> physicalChunk) {

		var removeBytesBefore = scavengePoint.Position - _retainBytes;
		return physicalChunk.ChunkEndPosition < removeBytesBefore;
	}

	private bool ShouldRemoveForPeriod(
		ScavengePoint scavengePoint,
		IScavengeStateForChunkExecutorWorker<TStreamId> concurrentState,
		IChunkReaderForExecutor<TStreamId, TRecord> physicalChunk) {

		for (var logicalChunkNumber = physicalChunk.ChunkEndNumber;
			logicalChunkNumber >= physicalChunk.ChunkStartNumber;
			logicalChunkNumber--) {

			if (concurrentState.TryGetChunkTimeStampRange(logicalChunkNumber, out var createdAtRange)) {
				var removeBefore = scavengePoint.EffectiveNow - _retainPeriod;
				return createdAtRange.Max < removeBefore;
			} else {
				// we don't have a time stamp range for this logical chunk, it had no prepares in during
				// accumulation. we try an earlier logical chunk in this physical chunk.
			}
		}

		// no time stamp for any logical chunk in this physical chunk. its possible to get here if the
		// physical chunk doesn't have any prepares in it at all. it's fine to remove it.
		return true;
	}

	private async ValueTask SwitchOutPhysicalChunk(
		IChunkReaderForExecutor<TStreamId, TRecord> physicalChunk,
		CancellationToken ct) {

		var chunkStartNumber = physicalChunk.ChunkStartNumber;
		var chunkEndNumber = physicalChunk.ChunkEndNumber;

		_logger.Debug(
			"SCAVENGING: Removing physical chunk: {oldChunkName} " +
			"{chunkStartNumber} => {chunkEndNumber} ({chunkStartPosition} => {chunkEndPosition})",
			physicalChunk.Name,
			chunkStartNumber, chunkEndNumber,
			physicalChunk.ChunkStartPosition, physicalChunk.ChunkEndPosition);

		var numChunks = chunkEndNumber - chunkStartNumber + 1;
		var locators = new string[numChunks];
		for (var i = 0; i < locators.Length; i++) {
			locators[i] = _locatorCodec.EncodeRemote(chunkStartNumber + i);
		}

		// begin the process of deleting the chunk
		var switched = await _chunkManager.SwitchInChunks(locators, ct);

		if (switched) {
			// the switch has occurred, the physicalChunk will be deleted once any outstanding
			// readers are returned.
		} else {
			// the start-end range did not align correctly, perhaps something else changed which physical
			// chunks are present. this does not cause a problem but it should be very rare.
			_logger.Warning(
				"SCAVENGING: Did not remove physicalChunk: {oldChunkName} " +
				"{chunkStartNumber} => {chunkEndNumber}. " +
				"This will be retried next scavenge.",
				physicalChunk.Name,
				chunkStartNumber, chunkEndNumber);
		}
	}
}
