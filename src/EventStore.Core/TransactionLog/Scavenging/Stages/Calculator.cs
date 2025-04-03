// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging.Stages;

public class Calculator<TStreamId> : ICalculator<TStreamId> {
	private readonly ILogger _logger;
	private readonly IIndexReaderForCalculator<TStreamId> _index;
	private readonly int _chunkSize;
	private readonly int _cancellationCheckPeriod;
	private readonly Buffer _buffer;
	private readonly Throttle _throttle;

	public class Buffer {
		public Buffer(int size) {
			Array = new(StreamHandle<TStreamId>, OriginalStreamData)[size];
		}

		public (StreamHandle<TStreamId>, OriginalStreamData)[] Array { get; init; }
	}

	public Calculator(
		ILogger logger,
		IIndexReaderForCalculator<TStreamId> index,
		int chunkSize,
		int cancellationCheckPeriod,
		Buffer buffer,
		Throttle throttle) {

		_logger = logger;
		_index = index;
		_chunkSize = chunkSize;
		_cancellationCheckPeriod = cancellationCheckPeriod;
		_buffer = buffer;
		_throttle = throttle;
	}

	public async ValueTask Calculate(
		ScavengePoint scavengePoint,
		IScavengeStateForCalculator<TStreamId> state,
		CancellationToken cancellationToken) {

		_logger.Debug("SCAVENGING: Started new scavenge calculation phase for {scavengePoint}",
			scavengePoint.GetName());

		var checkpoint = new ScavengeCheckpoint.Calculating<TStreamId>(
			scavengePoint: scavengePoint,
			doneStreamHandle: default);
		state.SetCheckpoint(checkpoint);
		await Calculate(checkpoint, state, cancellationToken);
	}

	public async ValueTask Calculate(
		ScavengeCheckpoint.Calculating<TStreamId> checkpoint,
		IScavengeStateForCalculator<TStreamId> state,
		CancellationToken cancellationToken) {

		_logger.Debug("SCAVENGING: Calculating from checkpoint: {checkpoint}", checkpoint);
		var stopwatch = Stopwatch.StartNew();

		var weights = new WeightAccumulator(state);
		var scavengePoint = checkpoint.ScavengePoint;
		var streamCalc = new StreamCalculator<TStreamId>(_index, scavengePoint);
		var eventCalc = new EventCalculator<TStreamId>(_chunkSize, _index, state, scavengePoint, streamCalc);

		var cancellationCheckCounter = 0;
		var periodStart = stopwatch.Elapsed;
		var totalCounter = 0;

		// iterate through the original (i.e. non-meta) streams that need scavenging (i.e.
		// those that have metadata or tombstones)
		// - for each one use the accumulated data to set/update the discard points of the stream.
		// - along the way add weight to the affected chunks.
		// note that the discard points will never discard the last event from the stream.
		// there are limited scenarios when we do want to do this, (metadata streams when the main
		// stream is tombstoned, and when UnsafeIgnoreHardDeletes is true) and they are governed by
		// the IsTombstoned flags.

		// we read a batch of streams at a time so that each time we commit there is no active read
		// prevents any risk of the WAL not being able to reset.
		static bool TryPopulateBuffer(
			IScavengeStateForCalculator<TStreamId> state,
			StreamHandle<TStreamId> checkpoint,
			(StreamHandle<TStreamId>, OriginalStreamData)[] buffer,
			out int count) {

			count = 0;
			var streams = state.OriginalStreamsToCalculate(checkpoint);
			foreach (var stream in streams) {
				buffer[count++] = stream;
				if (count == buffer.Length)
					break;
			}

			return count > 0;
		}

		var buffer = _buffer.Array;
		var cp = checkpoint?.DoneStreamHandle ?? default;
		while (TryPopulateBuffer(state, cp, buffer, out var countInBatch)) {
			// for determinism it is important that IncreaseChunkWeight is called in a transaction with
			// its calculation and checkpoint, otherwise the weight could be increased again on recovery
			var transaction = state.BeginTransaction();
			try {
				for (int i = 0; i < countInBatch; i++) {
					var (originalStreamHandle, originalStreamData) = buffer[i];
					if (originalStreamData.Status != CalculationStatus.Active)
						throw new InvalidOperationException(
							$"Attempted to calculate a {originalStreamData.Status} record: {originalStreamData}");

					streamCalc.SetStream(originalStreamHandle, originalStreamData);
					var newStatus = await streamCalc.CalculateStatus(cancellationToken);

					(var newDiscardPoint, var newMaybeDiscardPoint, cancellationCheckCounter) =
						await CalculateDiscardPointsForOriginalStream(
							eventCalc,
							weights,
							originalStreamHandle,
							scavengePoint,
							cancellationCheckCounter,
							cancellationToken);

					// don't allow the discard point to move backwards
					if (newDiscardPoint < originalStreamData.DiscardPoint) {
						newDiscardPoint = originalStreamData.DiscardPoint;
					}

					// don't allow the maybe discard point to move backwards
					if (newMaybeDiscardPoint < originalStreamData.MaybeDiscardPoint) {
						newMaybeDiscardPoint = originalStreamData.MaybeDiscardPoint;
					}

					if (newStatus == originalStreamData.Status &&
						newDiscardPoint == originalStreamData.DiscardPoint &&
						newMaybeDiscardPoint == originalStreamData.MaybeDiscardPoint) {

						// nothing to update for this stream
					} else {
						state.SetOriginalStreamDiscardPoints(
							streamHandle: originalStreamHandle,
							status: newStatus,
							discardPoint: newDiscardPoint,
							maybeDiscardPoint: newMaybeDiscardPoint);
					}

					totalCounter++;
				}

				// the last stream we processed is in the calculator
				cp = streamCalc.OriginalStreamHandle;
				weights.Flush();
				transaction.Commit(new ScavengeCheckpoint.Calculating<TStreamId>(
					scavengePoint,
					cp));

				var elapsed = stopwatch.Elapsed;
				LogRate($"period", countInBatch, elapsed - periodStart);
				LogRate("total", totalCounter, elapsed);

				periodStart = stopwatch.Elapsed;
			} catch (Exception ex) {
				if (ex is not OperationCanceledException) {
					_logger.Error(ex, "SCAVENGING: Rolling back");
				}
				// invariant: there is always an open transaction whenever an exception can be thrown
				transaction.Rollback();
				throw;
			}
		}

		LogRate("grand total (including rests)", totalCounter, stopwatch.Elapsed);
		_throttle.Rest(cancellationToken);
	}

	private void LogRate(string name, int count, TimeSpan elapsed) {
		var rate = count / elapsed.TotalSeconds;
		_logger.Debug(
			"SCAVENGING: Calculated in " + name + ": {count:N0} streams in {elapsed}. {rate:N2} streams per second",
			count, elapsed, rate);
	}

	// This does two things.
	// 1. Calculates and returns the discard points for this stream
	// 2. Adds weight to the affected chunks so that they get scavenged.
	//
	// The calculator determines that we can definitely discard everything up to the discardPoint
	// and we may be able to discard things between the discardPoint and the maybeDiscardPoint.
	//
	// We want to calculate the discard points from scratch, without considering what values they
	// came out as last time.
	private async ValueTask<(DiscardPoint DiscardPt, DiscardPoint MaybeDiscardPt, int CancellationCheckCounter)> CalculateDiscardPointsForOriginalStream(
		EventCalculator<TStreamId> eventCalc,
		WeightAccumulator weights,
		StreamHandle<TStreamId> originalStreamHandle,
		ScavengePoint scavengePoint,
		int cancellationCheckCounter,
		CancellationToken cancellationToken) {

		var fromEventNumber = 0L;

		var discardPoint = DiscardPoint.KeepAll;
		var maybeDiscardPoint = DiscardPoint.KeepAll;

		const int maxCount = 8192;

		var first = true;
		var allDiscardedSoFar = true;

		while (true) {
			// read in slices because the stream might be huge.
			// note: when the handle is a hash the ReadEventInfoForward call is index-only
			// note: the event infos are not necessarily contiguous
			var result = await _index.ReadEventInfoForward(
				originalStreamHandle,
				fromEventNumber,
				maxCount,
				scavengePoint,
				cancellationToken);

			var slice = result.EventInfos;

			foreach (var eventInfo in slice) {
				// Check cancellation and rest occasionally
				if (++cancellationCheckCounter == _cancellationCheckPeriod) {
					cancellationCheckCounter = 0;
					cancellationToken.ThrowIfCancellationRequested();
					_throttle.Rest(cancellationToken);
				}

				eventCalc.SetEvent(eventInfo);

				if (first) {
					// this is the first event that is known to the index. advance the discard points
					// to discard everything before here since they're already discarded. (we need
					// this because the chunks haven't necessarily been executed yet so we want to
					// make sure those records are removed when the chunks are scavenged. note that
					// chunk weight has already been added for them, so no need to do that again.
					discardPoint = DiscardPoint.DiscardBefore(eventInfo.EventNumber);
					maybeDiscardPoint = discardPoint;
					first = false;
				}

				switch (await eventCalc.DecideEvent(cancellationToken)) {
					case DiscardDecision.Discard:
						weights.OnDiscard(eventCalc.LogicalChunkNumber);
						discardPoint = DiscardPoint.DiscardIncluding(eventInfo.EventNumber);
						allDiscardedSoFar = true;
						break;

					case DiscardDecision.MaybeDiscard:
						// add weight to the chunk so that this will be inspected more closely
						// it is possible that we already added weight on a previous scavenge and are
						// doing so again, but we must because the weight may have been reset
						// by the previous scavenge
						weights.OnMaybeDiscard(eventCalc.LogicalChunkNumber);
						maybeDiscardPoint = DiscardPoint.DiscardIncluding(eventInfo.EventNumber);
						allDiscardedSoFar = false;
						break;

					case DiscardDecision.AlreadyDiscarded:
						// this event has already been deleted from the chunks but not from the index.
						// we move the discard point forward only if all events before it have been or will be discarded for the following reasons:
						//
						// i) usually, there should be no gaps between the event numbers of a stream but this property is not guaranteed
						// with the previous scavenger. if it happens that this event was in a stream gap and we always moved the discard point
						// forward, we would end up deleting all the events that were present before the gap.
						//
						// ii) we do our best to delete stale entries from the index

						if (allDiscardedSoFar)
							discardPoint = DiscardPoint.DiscardIncluding(eventInfo.EventNumber);
						break;

					case DiscardDecision.Keep:
						// found the first one to keep. we are done discarding. to help keep things
						// simple, move the maybe up to the discardpoint if it is behind.
						maybeDiscardPoint = maybeDiscardPoint.Or(discardPoint);
						return (discardPoint, maybeDiscardPoint, cancellationCheckCounter);

					default:
						throw new Exception(
							$"Invalid discard decision for stream {originalStreamHandle}.");
				}
			}

			// we haven't found an event to definitely keep
			if (result.IsEndOfStream) {
				// we have finished reading the stream from the index,
				// but not found any events to keep.
				// we therefore didn't find any at all, or found some and discarded them all
				// (the latter should not be possible)
				if (first) {
					// we didn't find any at all
					// - the stream might actually be empty
					// - the stream might have events after the scavenge point and old scavenge
					//   has removed the ones before
					// we didn't find anything to discard, so keep everything.
					return (DiscardPoint.KeepAll, DiscardPoint.KeepAll, cancellationCheckCounter);
				} else {
					// we found some and discarded them all, oops.
					throw new Exception(
						$"Calculated that all events for stream {originalStreamHandle} " +
						$"should be discarded. This should be impossible.");
				}
			} else {
				// we aren't done reading slices, read the next slice.
				fromEventNumber = result.NextEventNumber;
			}
		}
	}
}
