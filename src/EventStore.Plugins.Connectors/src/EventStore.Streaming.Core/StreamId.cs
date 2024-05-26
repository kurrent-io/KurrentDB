using static System.String;

namespace EventStore.Streaming;

[PublicAPI]
public readonly record struct StreamId : IComparable<StreamId>, IComparable {
	public static readonly StreamId None = new(null!);

	StreamId(string value) => Value = value;

	internal string Value { get; }

	public static StreamId From(string? value) {
		if (IsNullOrWhiteSpace(value)) throw new InvalidStreamId(value);

		return new(value);
	}

	public static implicit operator string(StreamId _) => _.Value;
	public static implicit operator StreamId(string _) => From(_);

	public override string ToString() => Value;

	#region . relational members .

	public int CompareTo(StreamId other) => Compare(Value, other.Value, StringComparison.Ordinal);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj)) return 1;

		return obj is StreamId other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(StreamId)}");
	}

	public static bool operator <(StreamId left, StreamId right)  => left.CompareTo(right) < 0;
	public static bool operator >(StreamId left, StreamId right)  => left.CompareTo(right) > 0;
	public static bool operator <=(StreamId left, StreamId right) => left.CompareTo(right) <= 0;
	public static bool operator >=(StreamId left, StreamId right) => left.CompareTo(right) >= 0;
	
	#endregion
}

public class InvalidStreamId(string? streamId) : Exception($"Stream id is {(IsNullOrWhiteSpace(streamId) ? "empty" : $"invalid: {streamId}")}");