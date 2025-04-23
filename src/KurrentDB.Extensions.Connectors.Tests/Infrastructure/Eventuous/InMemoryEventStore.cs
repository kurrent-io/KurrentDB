using System.Collections.Concurrent;
using EventStore.Streaming;

namespace Eventuous.Testing;

/// <summary>
/// In-memory event store implementation for testing purposes
/// </summary>
public class SystemInMemoryEventStore : IEventStore {
    readonly ConcurrentDictionary<StreamName, InMemoryStream> _storage = new();
    readonly List<StreamEvent>                                _global  = [];

    /// <inheritdoc />
    public Task<bool> StreamExists(StreamName streamName, CancellationToken cancellationToken) =>
        Task.FromResult(_storage.ContainsKey(streamName));

    /// <inheritdoc />
    public Task<AppendEventsResult> AppendEvents(
        StreamName stream,
        ExpectedStreamVersion expectedVersion,
        IReadOnlyCollection<NewStreamEvent> events,
        CancellationToken cancellationToken
    ) {
        var existing = _storage.GetOrAdd(stream, s => new InMemoryStream(s));
        existing.AppendEvents(expectedVersion, events);
        _global.AddRange(events);

        return Task.FromResult(new AppendEventsResult((ulong)(_global.Count - 1), existing.Version));
    }

    /// <inheritdoc />
    public Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken) {
        var result = FindStream(stream).GetEvents(start, count).ToArray();
        return result.Length == 0 ? throw new StreamNotFoundError(stream) : Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken) {
        var result = FindStream(stream).GetEventsBackwards(count).ToArray();
        return result.Length == 0 ? throw new StreamNotFoundError(stream) : Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task TruncateStream(
        StreamName stream,
        StreamTruncatePosition truncatePosition,
        ExpectedStreamVersion expectedVersion,
        CancellationToken cancellationToken
    ) {
        FindStream(stream).Truncate(expectedVersion, truncatePosition);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteStream(StreamName stream, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken) {
        var existing = FindStream(stream);
        existing.CheckVersion(expectedVersion);
        _storage.Remove(stream, out _);
        return Task.CompletedTask;
    }

    // ReSharper disable once ReturnTypeCanBeEnumerable.Local
    InMemoryStream FindStream(StreamName stream) => !_storage.TryGetValue(stream, out var existing) ? throw new StreamNotFound(stream) : existing;
}

record StoredEvent(StreamEvent Event, int Position);

class InMemoryStream(StreamName name) {
    public int Version { get; private set; } = -1;

    public string Name { get; } = name;

    readonly List<StoredEvent> _events = [];

    public void CheckVersion(ExpectedStreamVersion expectedVersion) {
        if (expectedVersion.Value != Version)
            throw new ExpectedStreamRevisionError(name, StreamRevision.From(expectedVersion.Value), StreamRevision.From(Version));
    }

    public void AppendEvents(ExpectedStreamVersion expectedVersion, IReadOnlyCollection<NewStreamEvent> events) {
        CheckVersion(expectedVersion);

        foreach (var streamEvent in events)
            _events.Add(new(streamEvent, ++Version));
    }

    public IEnumerable<StreamEvent> GetEvents(StreamReadPosition from, int count) {
        var selected = _events
            .SkipWhile(x => x.Position < from.Value);

        if (count > 0) selected = selected.Take(count);

        return selected.Select(x => x.Event with { Position = x.Position });
    }

    public IEnumerable<StreamEvent> GetEventsBackwards(int count) {
        var position = _events.Count - 1;

        while (count-- > 0)
            yield return _events[position--].Event;
    }

    public void Truncate(ExpectedStreamVersion version, StreamTruncatePosition position) {
        CheckVersion(version);
        _events.RemoveAll(x => x.Position <= position.Value);
    }
}