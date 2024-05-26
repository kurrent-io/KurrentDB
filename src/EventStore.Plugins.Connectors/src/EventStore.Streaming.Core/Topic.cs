// using static System.String;
//
// namespace EventStore.Streaming;

// [PublicAPI]
// public readonly record struct Topic : IComparable<Topic>, IComparable {
// 	public static readonly Topic None = new(null!);
//
// 	Topic(string value) => Value = value;
//
// 	internal string Value { get; }
//
// 	public static Topic From(string? value) {
// 		if (IsNullOrWhiteSpace(value)) throw new InvalidTopic(value);
//
// 		return new(value);
// 	}
//
// 	public static implicit operator string(Topic _) => _.Value;
// 	public static implicit operator Topic(string _) => From(_);
//
// 	public override string ToString() => Value;
//
// 	// public StreamFilter TopicFilter { get; init; }
// 	//
// 	// /// <summary>
// 	// /// so I either send a stream name or a stream name filter? jfc...
// 	// /// So on options I pass a stream name and then give a filter?
// 	// /// will get back to this later...
// 	// /// </summary>
// 	// public static StreamName Topic(ReadOnlySpan<char> streamName, char delimiter = '-', bool splitOnFirstDelimiter = true) {
// 	// 	if (streamName.IsEmpty) throw new InvalidStreamName(null);
// 	// 	
// 	// 	var index = splitOnFirstDelimiter 
// 	// 		? streamName.IndexOf(delimiter) 
// 	// 		: streamName.LastIndexOf(delimiter);
// 	//
// 	// 	var topic = streamName[..index];
// 	// 	
// 	// 	
// 	// 	
// 	// 	//var prefixes = Options.Streams.Select(stream => $"{stream}-").ToArray();
// 	// 	return new StreamName(streamName) {
// 	// 		TopicFilter = StreamFilter.Prefix(prefixes);
// 	// 	}
// 	// 	StreamFilter.RegularExpression("^account|^savings")
// 	// }
//
// 	#region . relational members .
//
// 	public int CompareTo(Topic other) => Compare(Value, other.Value, StringComparison.Ordinal);
//
// 	public int CompareTo(object? obj) {
// 		if (ReferenceEquals(null, obj)) return 1;
//
// 		return obj is Topic other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(Topic)}");
// 	}
//
// 	public static bool operator <(Topic left, Topic right)  => left.CompareTo(right) < 0;
// 	public static bool operator >(Topic left, Topic right)  => left.CompareTo(right) > 0;
// 	public static bool operator <=(Topic left, Topic right) => left.CompareTo(right) <= 0;
// 	public static bool operator >=(Topic left, Topic right) => left.CompareTo(right) >= 0;
// 	
// 	#endregion
// }
//
// public class InvalidTopic(string? topic) : Exception($"Topic is {(IsNullOrWhiteSpace(topic) ? "empty" : $"invalid: {topic}")}");
