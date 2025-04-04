// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.Stages;

// This calculates information about the event so that the main calculator can decide what to do
public class EventCalculator<TStreamId> {
	public EventCalculator(
		int chunkSize,
		IIndexReaderForCalculator<TStreamId> index,
		IScavengeStateForCalculatorReadOnly<TStreamId> state,
		ScavengePoint scavengePoint,
		StreamCalculator<TStreamId> streamCalc) {

		ChunkSize = chunkSize;
		Index = index;
		State = state;
		ScavengePoint = scavengePoint;
		Stream = streamCalc;
	}

	public void SetEvent(EventInfo eventInfo) {
		EventInfo = eventInfo;
	}

	// State that doesn't change. scoped to the scavenge.
	public int ChunkSize { get; }
	public IIndexReaderForCalculator<TStreamId> Index { get; }
	public IScavengeStateForCalculatorReadOnly<TStreamId> State { get; }
	public ScavengePoint ScavengePoint { get; }
	public StreamCalculator<TStreamId> Stream { get; }

	// State that is scoped to the event.
	public EventInfo EventInfo { get; private set; }

	public bool IsOnOrAfterScavengePoint => EventInfo.LogPosition >= ScavengePoint.Position;

	public int LogicalChunkNumber => (int)(EventInfo.LogPosition / ChunkSize);

	public async ValueTask<DiscardDecision> DecideEvent(CancellationToken token) {
		// Events in original streams can be discarded because of:
		//   Tombstones, TruncateBefore, MaxCount, MaxAge.
		//
		// any one of these is enough to warrant a discard
		// however none of them allow us to scavenge the last event
		// or anything beyond the scavenge point, so we limit by that.

		// respect the scavenge point
		if (IsOnOrAfterScavengePoint) {
			return DiscardDecision.Keep;
		}

		// keep last event in stream
		// to be extra safe, we keep if it is after the 'last event' too, which should never happen.
		if (EventInfo.EventNumber >= await Stream.GetLastEventNumber(token)) {
			return DiscardDecision.Keep;
		}

		// for tombstoned streams, discard everything before the tombstone
		if (Stream.IsTombstoned) {
			// the tombstone is nearly always the last event and therefore already kept above ^
			// BUT if the tombstone was created when event numbers were 32bit, and the index has
			// not been _rebuilt_ since event numbers have been 64bit (merges and scavenges are not
			// sufficient) then the tombstone will still appear in the index as having event number
			// int.max instead of long.max. the system does not treat such a stream as deleted, so
			// more events could be written and read after the tombstone, but rebuilding the index
			// will make the whole stream deleted again.
			//
			// to avoid complicating this further in scavenge, we keep an eye out for such tombstones
			// and keep the tombstone and any the events after it. without this check we would
			// discard the tombstone and subsequent events except the last one, leaving the stream
			// in a state where it is unclear why events were removed.
			if (EventInfo.EventNumber == int.MaxValue && await Index.IsTombstone(EventInfo.LogPosition, token)) {
				return DiscardDecision.Keep;
			}

			// we already know this is not the last event, so discard it.
			return DiscardDecision.Discard;
		}

		// truncatebefore, maxcount
		if ((await Stream.GetTruncateBeforeOrMaxCountDiscardPoint(token)).ShouldDiscard(EventInfo.EventNumber)) {
			return DiscardDecision.Discard;
		}

		// up to maxage. keep if there is no maxage restriction
		var cutoffTime = Stream.CutoffTime;
		if (!cutoffTime.HasValue) {
			return DiscardDecision.Keep;
		}

		// there is a maxage restriction
		return ShouldDiscardForMaxAge(cutoffTime.Value);
	}

	private DiscardDecision ShouldDiscardForMaxAge(DateTime cutoffTime) {
		// establish a range that the event was definitely created between.
		if (!State.TryGetChunkTimeStampRange(LogicalChunkNumber, out var createdAtRange)) {
			// we don't have a time stamp range for this chunk which implies that it was empty during accumulation.
			// however while reading event infos from the index, we encountered an event from that chunk.
			// this indicates that the event was deleted from the chunk but not from the index by the old scavenger.
			return DiscardDecision.AlreadyDiscarded;
		}

		// range is guaranteed to be non-empty
		if (cutoffTime <= createdAtRange.Min) {
			// if the cutoff time is equal to the minimum then the record timestamp is definitely
			// greater than or equal to the cutoff, so we keep it
			return DiscardDecision.Keep;
		}

		if (createdAtRange.Max < cutoffTime) {
			return DiscardDecision.Discard;
		}

		// it must be between min and max inclusive
		return DiscardDecision.MaybeDiscard;
	}
}
