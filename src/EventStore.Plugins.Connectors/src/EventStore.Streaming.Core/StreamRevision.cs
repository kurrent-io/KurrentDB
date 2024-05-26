namespace EventStore.Streaming;

[PublicAPI]
public readonly record struct StreamRevision : IComparable<StreamRevision>, IComparable {
	/// <summary>
	/// A special value that refers to an invalid, unassigned or default revision.
	/// </summary>
	public static readonly StreamRevision Unset = new(-1);
	
	/// <summary>
	/// The beginning (i.e., the first event) of a stream.
	/// </summary>
	public static readonly StreamRevision Min = new(0);
	
	/// <summary>
	/// The end of a stream. Use this when reading a stream backwards, or subscribing live to a stream.
	/// </summary>
	public static readonly StreamRevision Max = new(long.MaxValue);
	
	StreamRevision(long value) => Value = value;

	internal long Value { get; }

	public static StreamRevision From(long value) {
		if (value < 0 && value != Unset.Value)
			throw new InvalidStreamRevision(value);
		
		return new(value);
	}
	
	public static implicit operator long(StreamRevision _) => _.Value;
	public static implicit operator StreamRevision(long _) => From(_);

	public override string ToString() => Value.ToString();
	
	#region . relational members .

	public int CompareTo(StreamRevision other) => Value.CompareTo(other.Value);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj)) return 1;

		return obj is StreamRevision other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(StreamRevision)}");
	}

	public static bool operator <(StreamRevision left, StreamRevision right) => left.CompareTo(right) < 0;
	public static bool operator >(StreamRevision left, StreamRevision right) => left.CompareTo(right) > 0;
	public static bool operator <=(StreamRevision left, StreamRevision right) => left.CompareTo(right) <= 0;
	public static bool operator >=(StreamRevision left, StreamRevision right) => left.CompareTo(right) >= 0;

	#endregion
}

public class InvalidStreamRevision(long? value) : Exception($"Stream revision is invalid: {value}");
