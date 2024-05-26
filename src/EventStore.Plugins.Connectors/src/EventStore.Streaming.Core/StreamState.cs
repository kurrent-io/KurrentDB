namespace EventStore.Streaming;

/// <summary>
/// Represents the state of a stream in the Event Store.
/// </summary>
public enum StreamState {
    /// <summary>
    /// The stream is missing or does not exist.
    /// </summary>
    Missing = 1,

    /// <summary>
    /// The stream can be in any state. This is typically used for filters or conditions.
    /// </summary>
    Any = 2,

    /// <summary>
    /// The stream exists.
    /// </summary>
    Exists = 4,
}