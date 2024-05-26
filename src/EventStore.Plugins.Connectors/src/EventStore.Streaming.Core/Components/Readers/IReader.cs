using EventStore.Streaming.Consumers;

namespace EventStore.Streaming.Readers;

public interface IReaderMetadata {
    /// <summary>
    /// The name of the reader.
    /// </summary>
    public string ReaderName { get; }
}

public interface IReader : IReaderMetadata, IAsyncDisposable {
    /// <summary>
    /// Reads records from the EventStore
    /// </summary>
    /// <returns>
    ///	A sequence of <see cref="EventStoreRecord" />.
    /// </returns>
    public IAsyncEnumerable<EventStoreRecord> Read(
        LogPosition position, ReadDirection direction, ConsumeFilter filter, int maxCount, CancellationToken cancellationToken = default
    );
}