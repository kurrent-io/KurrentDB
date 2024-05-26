namespace EventStore.Streaming;

/// <summary>
/// Record position in the global log.
/// </summary>
[PublicAPI]
public readonly record struct LogPosition : IComparable<LogPosition>, IComparable {
	public static readonly LogPosition Unset;
	
	/// <summary>
	/// Initial position in the transaction file that is used when no other position is specified.
	/// </summary>
	public static readonly LogPosition Earliest; // this is the fromAll start position... or earliest ffs
	
	/// <summary>
	/// Initial position in the transaction file that represents the end of the transaction file.
	/// </summary>
	public static readonly LogPosition Latest = new(ulong.MaxValue, ulong.MaxValue);

	LogPosition(ulong? commitPosition, ulong? preparePosition) {
		CommitPosition  = commitPosition;
		PreparePosition = preparePosition;
	}

	public ulong? CommitPosition  { get; }
	public ulong? PreparePosition { get; }

	public static LogPosition From(ulong? commitPosition, ulong? preparePosition) {
		if (commitPosition < preparePosition)
			throw new InvalidLogPosition(new(commitPosition, preparePosition));
		
		if (commitPosition > long.MaxValue && commitPosition != ulong.MaxValue)
			throw new InvalidLogPosition(new(commitPosition, preparePosition));
		
		if (preparePosition > long.MaxValue && preparePosition != ulong.MaxValue)
			throw new InvalidLogPosition(new(commitPosition, preparePosition));

		if (commitPosition is not null && preparePosition is null)
			throw new InvalidLogPosition(new(commitPosition, preparePosition));
		
		if (commitPosition is null && preparePosition is not null)
			throw new InvalidLogPosition(new(commitPosition, preparePosition));
		
		return new(commitPosition, preparePosition);
	}

	public static LogPosition From(ulong? position) => From(position, position);
	
	public static LogPosition From(long? commitPosition, long? preparePosition) =>
		From((ulong?)commitPosition, (ulong?)preparePosition);
	
	public static implicit operator ulong?(LogPosition _) => _.CommitPosition;
	public static implicit operator LogPosition(ulong? _) => From(_, _);
	
	public override string ToString() =>
		$"{CommitPosition?.ToString() ?? "-"}/{PreparePosition?.ToString() ?? "-"}";

	#region . relational members .

	public int CompareTo(LogPosition other) {
		var commitPositionComparison = Nullable.Compare(CommitPosition, other.CommitPosition);
		if (commitPositionComparison != 0) return commitPositionComparison;
		
		return Nullable.Compare(PreparePosition, other.PreparePosition);
	}

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj)) return 1;

		return obj is LogPosition other 
			? CompareTo(other) 
			: throw new ArgumentException($"Object must be of type {nameof(LogPosition)}");
	}

	public static bool operator <(LogPosition left, LogPosition right)  => left.CompareTo(right) < 0;
	public static bool operator >(LogPosition left, LogPosition right)  => left.CompareTo(right) > 0;
	public static bool operator <=(LogPosition left, LogPosition right) => left.CompareTo(right) <= 0;
	public static bool operator >=(LogPosition left, LogPosition right) => left.CompareTo(right) >= 0;

	#endregion
}

public class InvalidLogPosition(LogPosition value) : ArgumentException($"Log position is invalid: {value.ToString()}");