namespace EventStore.Streaming;

[PublicAPI]
public readonly record struct SequenceId : IComparable<SequenceId>, IComparable {
	public static readonly SequenceId None = new(0);
	
	SequenceId(ulong value) => Value = value;

	internal ulong Value { get; }

	public static SequenceId From(ulong value) => new(value);

	public static implicit operator ulong(SequenceId _) => _.Value;
	public static implicit operator SequenceId(ulong _) => From(_);

	public override string ToString() => Value.ToString();
	
	#region . relational members .

	public int CompareTo(SequenceId other) => Value.CompareTo(other.Value);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		return obj is SequenceId other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(SequenceId)}");
	}

	public static bool operator <(SequenceId left, SequenceId right)  => left.CompareTo(right) < 0;
	public static bool operator >(SequenceId left, SequenceId right)  => left.CompareTo(right) > 0;
	public static bool operator <=(SequenceId left, SequenceId right) => left.CompareTo(right) <= 0;
	public static bool operator >=(SequenceId left, SequenceId right) => left.CompareTo(right) >= 0;

	#endregion
}

sealed class SequenceIdGenerator(ulong initialValue = 1) {
	// Subtracting one because Interlocked.Increment will return the post-incremented value
	// which is expected to be the initialSequenceId for the first call
	long _actual = unchecked((long)initialValue - 1);
	
	public SequenceId FetchNext() => unchecked((ulong)Interlocked.Increment(ref _actual));
}
