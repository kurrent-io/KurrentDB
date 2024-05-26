namespace EventStore.Streaming.Consumers.Checkpoints;

public readonly record struct TrackedPosition(SequenceId SequenceId, RecordPosition Position) : IComparable<TrackedPosition>, IComparable {
	public static readonly TrackedPosition None = new(SequenceId.None, RecordPosition.Unset);
	
	public static implicit operator TrackedPosition(EventStoreRecord _) => new TrackedPosition(_.SequenceId, _.Position);
	
	#region . relational members .

	public int CompareTo(TrackedPosition other) {
        var sequenceIdComparison = SequenceId.CompareTo(other.SequenceId);
        return sequenceIdComparison != 0 ? sequenceIdComparison : Position.CompareTo(other.Position);
    }

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		return obj is TrackedPosition other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(TrackedPosition)}");
	}

	public static bool operator <(TrackedPosition left, TrackedPosition right)  => left.CompareTo(right) < 0;
	public static bool operator >(TrackedPosition left, TrackedPosition right)  => left.CompareTo(right) > 0;
	public static bool operator <=(TrackedPosition left, TrackedPosition right) => left.CompareTo(right) <= 0;
	public static bool operator >=(TrackedPosition left, TrackedPosition right) => left.CompareTo(right) >= 0;

	#endregion
}