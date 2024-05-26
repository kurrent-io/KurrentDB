using System.Runtime.CompilerServices;

namespace EventStore.Streaming.Consumers.Checkpoints;

[PublicAPI]
class TrackedPositionSet : SortedSet<TrackedPosition> {
	public TrackedPositionSet() { }

	public TrackedPositionSet(TrackedPositionSet otherSet) : base(otherSet.ToArray()) {
		LastFlushedPosition = otherSet.LastFlushedPosition;
		NextReadyPosition   = otherSet.NextReadyPosition;
		FirstGap            = otherSet.FirstGap;
	}

	public TrackedPosition LastFlushedPosition { get; private set; } = TrackedPosition.None;
	public TrackedPosition NextReadyPosition   { get; private set; } = TrackedPosition.None;
	public ulong           FirstGap            { get; private set; }

	public bool GapDetected => FirstGap > 0;
	
	static readonly object Locker = new();

	public new void Add(TrackedPosition position) {
		if (position == TrackedPosition.None)
			throw new ArgumentNullException(nameof(position));
		
		if (position <= LastFlushedPosition)
			throw new InvalidOperationException(
				$"Position {position} @ {position.SequenceId} must be higher than last flushed"
			  + $" position {LastFlushedPosition} @ {LastFlushedPosition.SequenceId}");
		
		bool added;
		try {
			added = base.Add(position);
		}
		catch (Exception ex) {
			throw new Exception($"No idea why it would fail to add the position with sequence id: {position.SequenceId}", ex);
		}
		
		if (!added)
			throw new InvalidOperationException($"Position {position} @ {position.SequenceId} already tracked");

		PrepareNextReadyPosition();
	}

	/// <summary>
	/// Clears the set of positions that have been committed.
	/// </summary>
	public void Flush() {
		lock (Locker) {
			// nothing to flush regardless of the number of tracked positions
			if (NextReadyPosition == TrackedPosition.None)
				return;

			// set the last flushed position to the next ready position
			LastFlushedPosition = NextReadyPosition;
		
			// reset the next ready position
			NextReadyPosition = TrackedPosition.None;
		
			// remove all positions that are less than or equal to the last flushed position
			RemoveWhere(x => x.SequenceId <= LastFlushedPosition.SequenceId);
		}
	}
	
	public TrackedPositionSet Clone() => new(this);

	void PrepareNextReadyPosition() {
		var (nextReadyPosition, gap) = GetNextReadyPosition(this, LastFlushedPosition);

		NextReadyPosition = nextReadyPosition;
		FirstGap          = gap;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static (TrackedPosition ReadyPosition, ulong Gap) GetNextReadyPosition(SortedSet<TrackedPosition> trackedPositions, TrackedPosition lastFlushedPosition) {
		var positions = new SortedSet<TrackedPosition>(
			lastFlushedPosition != TrackedPosition.None
				? new[] { lastFlushedPosition }.Union(trackedPositions)
				: trackedPositions
		);
		
		switch (positions.Count) {
			// no positions, return none
			case 0: return (TrackedPosition.None, 0U);
			
			// one position, return it if it's not the last flushed position
			case 1: return positions.Min != lastFlushedPosition ? (positions.Min, 0U) : (TrackedPosition.None, 0U); 

			// more than one position, find the first gap
			default: {
				var gap = positions
					.Zip(positions.Skip(1), Tuple.Create)
					.FirstOrDefault(x => x.Item1.SequenceId + 1 != x.Item2.SequenceId);

				// no gap found use last tracked position
				if (gap is null)
					return (positions.Max, 0U);

				// gap found, return the position before the gap
				// except if it's the last flushed position
				return gap.Item1 != lastFlushedPosition
					? (gap.Item1, gap.Item2.SequenceId - gap.Item1.SequenceId - 1)
					: (TrackedPosition.None, gap.Item2.SequenceId - gap.Item1.SequenceId - 1);
			}
		}
	}
}