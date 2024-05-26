namespace EventStore.Streaming;

/// thinking....
/// 
/// topic: product
/// partitions: 3
///
/// What streams should we create in ES:
/// "product-0"
/// "product-1"
///	"product-2"
/// -- "product-95D6A785-47AD-448D-9C9A-DDEA0E888CD5"
/// 
/// Appending to a stream: "ProductRegistered( name: iphone 13 pro id: 95D6A785-47AD-448D-9C9A-DDEA0E888CD5 price: 999.99)"
/// Message:: Value: "ProductRegistered( name: iphone 13 pro id: 95D6A785-47AD-448D-9C9A-DDEA0E888CD5)", PartitionKey: "95D6A785-47AD-448D-9C9A-DDEA0E888CD5"
/// var partitionId = HashGenerators.MurmurHash3(partitionKey) % Options.PartitionCount;

/// <summary>
/// Represents the position of the record in the system.
/// </summary>
[PublicAPI]
public readonly record struct RecordPosition() : IComparable<RecordPosition>, IComparable {
	public static readonly RecordPosition Unset;
	
	public StreamId       StreamId       { get; init; } = StreamId.None;
	public StreamRevision StreamRevision { get; init; } = StreamRevision.Unset;
	public LogPosition    LogPosition    { get; init; } = LogPosition.Latest;   
	public PartitionId    PartitionId    { get; init; } = PartitionId.Any;

	public bool IsTopicPosition => PartitionId > PartitionId.Any;
	
	public override string ToString() => PartitionId == PartitionId.Any 
		? $"{StreamId}:{LogPosition.CommitPosition}"
		: $"{StreamId}:{PartitionId}:{LogPosition.CommitPosition?.ToString() ?? "-"}";
	
	#region . relational members .

	public int CompareTo(RecordPosition other) => LogPosition.CompareTo(other.LogPosition);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		return obj is RecordPosition other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(RecordPosition)}");
	}

	public static bool operator <(RecordPosition left, RecordPosition right) => left.CompareTo(right) < 0;
	public static bool operator >(RecordPosition left, RecordPosition right) => left.CompareTo(right) > 0;
	public static bool operator <=(RecordPosition left, RecordPosition right) => left.CompareTo(right) <= 0;
	public static bool operator >=(RecordPosition left, RecordPosition right) => left.CompareTo(right) >= 0;

	#endregion
}