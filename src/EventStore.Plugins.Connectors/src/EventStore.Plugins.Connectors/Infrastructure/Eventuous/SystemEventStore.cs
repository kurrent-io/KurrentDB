using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using EventStore.Streaming.Schema;
using Eventuous;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.Eventuous;

public class SystemEventStore : IEventStore {
    public SystemEventStore(
        SystemReader reader,
        SystemProducer producer,
        ILoggerFactory? loggerFactory = null
    ) {
        Reader   = reader;
        Producer = producer;
        Logger   = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SystemEventStore>();
    }

    ILogger        Logger   { get; }
    SystemReader   Reader   { get; }
    SystemProducer Producer { get; }

    /// <inheritdoc/>
    public async Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken) {
        var exists = await Reader
            .ReadBackwards(LogPosition.Latest, ConsumeFilter.Streams(stream), 1, cancellationToken: cancellationToken)
            .AnyAsync(cancellationToken: cancellationToken);

        return exists; // lol, not that easy yet, but it should be.
    }

    /// <inheritdoc/>
    public async Task<AppendEventsResult> AppendEvents(
        StreamName stream,
        ExpectedStreamVersion expectedVersion,
        IReadOnlyCollection<StreamEvent> events,
        CancellationToken cancellationToken
    ) {
        var messages = events.Select(
            evt => {
                ArgumentNullException.ThrowIfNull(evt.Payload, nameof(evt.Payload)); // must do it better

                var headers    = (Headers)evt.Metadata.ToHeaders();
                var schemaInfo = SchemaInfo.FromContentType(evt.Payload.GetType().FullName!, evt.ContentType);

                var message = Message.Builder
                    .RecordId(evt.Id)
                    .Value(evt.Payload)
                    .Headers(headers)
                    .WithSchema(schemaInfo)
                    .Create();

                return message;
            }
        ).ToArray();

        var requestbuilder = SendRequest.Builder
            .Stream(stream)
            .Messages(messages);

        if (expectedVersion == ExpectedStreamVersion.NoStream)
            requestbuilder = requestbuilder.ExpectedStreamState(StreamState.Missing);
        else if (expectedVersion == ExpectedStreamVersion.Any)
            requestbuilder = requestbuilder.ExpectedStreamState(StreamState.Any);
        else
            requestbuilder = requestbuilder.ExpectedStreamRevision(StreamRevision.From(expectedVersion.Value));

        var request = requestbuilder.Create();

        var result = await Producer.Send(request);

        return result switch {
            { Success: true } =>
                new AppendEventsResult(
                    (ulong)result.Position.LogPosition.CommitPosition!,
                    result.Position.StreamRevision
                ),
            { Error: StreamNotFoundError } => throw new StreamNotFound(stream),
            { Error: not null }            => throw new AppendToStreamException($"Unable to appends events to {stream}", result.Error),
        };
    }

    /// <inheritdoc/>
    public async Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken) {
        var from = start.Value == 0
            ? LogPosition.Earliest
            : LogPosition.From((ulong?)start.Value);

        try {
            var result = await Reader
                .ReadForwards(from, ConsumeFilter.Streams(stream), count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.Subject))
                .Select(
                    record => new StreamEvent(
                        record.Id,
                        record.Value,
                        Metadata.FromHeaders(record.Headers),
                        record.SchemaInfo.ContentType,
                        (long)record.Position.LogPosition.CommitPosition!.Value
                    )
                )
                .ToArrayAsync(cancellationToken);

            return result;
        }
        catch (Exception ex) {
            throw new ReadFromStreamException($"Unable to read {count} starting at {start} events from {stream}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, int count, CancellationToken cancellationToken) {
        try {
            var result = await Reader
                .ReadBackwards(ConsumeFilter.Streams(stream), count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.Subject))
                .Select(
                    record => new StreamEvent(
                        record.Id,
                        record.Value,
                        Metadata.FromHeaders(record.Headers),
                        record.SchemaInfo.ContentType,
                        (long)record.Position.LogPosition.CommitPosition!.Value
                    )
                )
                .ToArrayAsync(cancellationToken);

            return result;
        }
        catch (Exception ex) {
            throw new ReadFromStreamException($"Unable to read {count} events backwards from {stream}", ex);
        }
    }

    /// <inheritdoc/>
    public Task TruncateStream(
        StreamName stream,
        StreamTruncatePosition truncatePosition,
        ExpectedStreamVersion expectedVersion,
        CancellationToken cancellationToken
    ) {
        throw new NotImplementedException();
        //new TruncateStreamException($"Unable to truncate stream {stream} at {truncatePosition}");
    }

    /// <inheritdoc/>
    public Task DeleteStream(StreamName stream, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken) {
        throw new NotImplementedException();
        //new DeleteStreamException($"Unable to delete stream {stream}");
    }
}