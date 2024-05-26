using EventStore.Streaming.Consumers;

namespace EventStore.Streaming.Readers;

public static class ReaderExtensions {
    public static IAsyncEnumerable<EventStoreRecord> Read(
        this IReader reader, LogPosition from, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Forwards, ConsumeFilter.None, maxCount, cancellationToken);

    public static IAsyncEnumerable<EventStoreRecord> Read(
        this IReader reader, LogPosition from, string[] streams, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Forwards, ConsumeFilter.Streams(streams), maxCount, cancellationToken);

    public static IAsyncEnumerable<EventStoreRecord> ReadForwards(
        this IReader reader, LogPosition from, ConsumeFilter filter, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Forwards, filter, maxCount, cancellationToken);
    
    public static IAsyncEnumerable<EventStoreRecord> ReadForwards(
        this IReader reader, ConsumeFilter filter, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(LogPosition.Earliest, ReadDirection.Forwards, filter, maxCount, cancellationToken);

    public static IAsyncEnumerable<EventStoreRecord> ReadBackwards(
        this IReader reader, LogPosition from, ConsumeFilter filter, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Backwards, filter, maxCount, cancellationToken);
    
    public static IAsyncEnumerable<EventStoreRecord> ReadBackwards(
        this IReader reader, ConsumeFilter filter, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(LogPosition.Latest, ReadDirection.Backwards, filter, maxCount, cancellationToken);

    public static IAsyncEnumerable<EventStoreRecord> ReadForwards(
        this IReader reader, LogPosition from, string[] streams, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Forwards, ConsumeFilter.Streams(streams), maxCount, cancellationToken);

    public static IAsyncEnumerable<EventStoreRecord> ReadBackwards(
        this IReader reader, LogPosition from, string[] streams, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Backwards, ConsumeFilter.Streams(streams), maxCount, cancellationToken);
    
    public static IAsyncEnumerable<EventStoreRecord> ReadForwards(
        this IReader reader, LogPosition from, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Forwards, ConsumeFilter.None, maxCount, cancellationToken);

    public static IAsyncEnumerable<EventStoreRecord> ReadBackwards(
        this IReader reader, LogPosition from, int maxCount = int.MaxValue, CancellationToken cancellationToken = default
    ) =>
        reader.Read(from, ReadDirection.Backwards,  ConsumeFilter.None, maxCount, cancellationToken);
}