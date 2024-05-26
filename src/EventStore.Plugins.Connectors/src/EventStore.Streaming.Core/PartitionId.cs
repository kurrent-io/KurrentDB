namespace EventStore.Streaming;

[PublicAPI]
public readonly record struct PartitionId : IComparable<PartitionId>, IComparable {
	public static readonly PartitionId Any = new(-1);

	PartitionId(long value) => Value = value;

	internal long Value { get; }

	public static PartitionId From(long value) {
		if (value < 0 && value != Any.Value)
			throw new InvalidPartitionId(value);
		
		return new(value);
	}

	public static implicit operator long(PartitionId _) => _.Value;
	public static implicit operator PartitionId(long _) => From(_);

	public override string ToString() => Value.ToString();

	#region . relational members .
	
	public int CompareTo(PartitionId other) => Value.CompareTo(other.Value);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj)) return 1;

		return obj is PartitionId other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(PartitionId)}");
	}

	public static bool operator <(PartitionId left, PartitionId right)  => left.CompareTo(right) < 0;
	public static bool operator >(PartitionId left, PartitionId right)  => left.CompareTo(right) > 0;
	public static bool operator <=(PartitionId left, PartitionId right) => left.CompareTo(right) <= 0;
	public static bool operator >=(PartitionId left, PartitionId right) => left.CompareTo(right) >= 0;
	
	#endregion
}

public class InvalidPartitionId(long? value) : ArgumentException($"Partition id is invalid: {value}");