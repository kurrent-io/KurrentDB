namespace EventStore.Streaming.Readers;

/// <summary>
/// An enumeration that indicates the direction of the read operation.
/// </summary>
public enum ReadDirection {
	/// <summary>Read backwards.</summary>
	Backwards,

	/// <summary>Read forwards.</summary>
	Forwards,
}