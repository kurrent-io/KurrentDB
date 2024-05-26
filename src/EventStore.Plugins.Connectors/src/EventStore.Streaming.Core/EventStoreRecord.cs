using EventStore.Streaming.Schema;

namespace EventStore.Streaming;

[PublicAPI]
public readonly record struct EventStoreRecord() {
	public static readonly EventStoreRecord None;
	
	/// <summary>
	/// Unique uuid, equivalent to an entry id in the log.
	/// </summary>
	public RecordId Id { get; init; } = RecordId.None;

	/// <summary>
	/// The position of the record in the stream.
	/// </summary>
	public RecordPosition Position { get; init; } = RecordPosition.Unset;

	/// <summary>
	/// When the record was created in the database.
	/// </summary>
	public DateTime Timestamp { get; init; } = default;
	
	/// <summary>
	/// The schema of the message.
	/// </summary>
	public SchemaInfo SchemaInfo { get; init; } = SchemaInfo.None;
	
	/// <summary>
	/// If the record is redacted, then the message will be empty.
	/// </summary>
	public bool IsRedacted { get; init; }

	/// <summary>
	/// The consume sequence number.
	/// </summary>
	public SequenceId SequenceId { get; init; } = SequenceId.None; 

	/// <summary>
	/// The headers of the message. This is the metadata of the message deserialized.
	/// Can the decoded metadata include our own custom headers? I think so.
	/// </summary>
	public Headers Headers { get; init; } = new();

	/// <summary>
	/// The deserialized message. 
	/// </summary>
	public object Value { get; init; } = null!;
	
	/// <summary>
	/// The type of the deserialized message.
	/// </summary>
	public Type ValueType => Value?.GetType() ?? Type.Missing.GetType();

	/// <summary>
	///  The raw data of the message.
	/// </summary>
	public ReadOnlyMemory<byte> Data { get; init; } = ReadOnlyMemory<byte>.Empty;
	
	/// <summary>
	/// The id of the request that produced the message.
	/// </summary>
	public Guid RequestId => Headers.TryGetValue(HeaderKeys.ProducerRequestId, out var value)
		? Guid.TryParse(value!, out var guid) ? guid : Guid.Empty
		: Guid.Empty;
	
	/// <summary>
	/// The name of the producer that produced the message.
	/// </summary>
	public string ProducerName => Headers.TryGetValue(HeaderKeys.ProducerName, out var value)
		? value! 
		: string.Empty;
	
	/// <summary>
	/// The name of the producer that produced the message.
	/// </summary>
	public PartitionKey Key => Headers.TryGetValue(HeaderKeys.PartitionKey, out var value)
		? PartitionKey.From(value) 
		: PartitionKey.None;

	public bool HasRequestId    => RequestId != Guid.Empty;
	public bool HasProducerName => ProducerName != string.Empty;
	public bool HasPartitionKey => Key != PartitionKey.None;
	public bool HasValue        => Value != null;
	public bool IsDecoded       => Value != null && Data.Length > 0;

	public StreamId    StreamId    => Position.StreamId;
	public PartitionId PartitionId => Position.PartitionId;
	public LogPosition LogPosition => Position.LogPosition;

	public override string ToString() => $"{Id} {Position} {SchemaInfo.Subject}";
}