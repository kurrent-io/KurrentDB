using EventStore.Connect.Producers;
using EventStore.Connect.Readers;
using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using EventStore.Streaming.Schema;
using Eventuous;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.Eventuous;

static class SchemaRegistryExtensions {
    public static async Task<RegisteredSchema> GetOrRegister(
        this SchemaRegistry schemaRegistry, Type messageType, SchemaDefinitionType schemaType, CancellationToken cancellationToken = default
    ) => await schemaRegistry.RegisterSchema(SchemaInfo.FromMessageType(messageType, schemaType), messageType, cancellationToken);
}

[UsedImplicitly]
public class SystemEventStore : IEventStore, IAsyncDisposable {
    public SystemEventStore(SystemReader reader, SystemProducer producer, SchemaRegistry schemaRegistry, ILoggerFactory? loggerFactory = null) {
        Reader         = reader;
        Producer       = producer;
        SchemaRegistry = schemaRegistry;
        Logger         = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SystemEventStore>();
    }

    public SystemEventStore(IPublisher publisher, SchemaRegistry schemaRegistry, ILoggerFactory? loggerFactory = null) {
        SchemaRegistry =   schemaRegistry;
        loggerFactory  ??= NullLoggerFactory.Instance;

        Reader = SystemReader.Builder
            .ReaderId("rdx-eventuous")
            .Publisher(publisher)
            .SchemaRegistry(schemaRegistry)
            .LoggerFactory(loggerFactory)
            .Create();

        Producer = SystemProducer.Builder
            .ProducerId("pdx-eventuous")
            .Publisher(publisher)
            .SchemaRegistry(schemaRegistry)
            .LoggerFactory(loggerFactory)
            .Create();

        Logger = loggerFactory.CreateLogger<SystemEventStore>();
    }

    SchemaRegistry SchemaRegistry { get; }
    ILogger        Logger         { get; }
    SystemReader   Reader         { get; }
    SystemProducer Producer       { get; }

    /// <inheritdoc/>
    public async Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken = default) {
        try {
            var exists = await Reader
                .ReadBackwards(LogPosition.Latest, ConsumeFilter.Streams(stream), 1, cancellationToken)
                .AnyAsync(cancellationToken);

            return exists;
        } catch (Exception ex) when (ex is not StreamingError) {
            throw new StreamingCriticalError($"Unable to check if stream {stream} exists", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<AppendEventsResult> AppendEvents(
        StreamName stream,
        ExpectedStreamVersion expectedVersion,
        IReadOnlyCollection<StreamEvent> events,
        CancellationToken cancellationToken = default
    ) {
        List<Message> messages = [];

        foreach (var evt in events) {
            ArgumentNullException.ThrowIfNull(evt.Payload, nameof(evt.Payload)); // must do it better

            var headers = new Headers();
            foreach (var kvp in evt.Metadata.ToHeaders())
                headers.Add(kvp.Key, kvp.Value);

            // // TODO SS: the producer should handle this no? actually its the serializer. why is the schema info not being set?
            // var schema = await SchemaRegistry
            //     .GetOrRegister(evt.Payload.GetType(), SchemaDefinitionType.Json, cancellationToken);
            //
            // var schemaInfo = new SchemaInfo(schema.Subject, SchemaDefinitionType.Json);

            var message = Message.Builder
                .RecordId(evt.Id)
                .Value(evt.Payload)
                .Headers(headers)
                .WithSchemaType(SchemaDefinitionType.Json)
                // .WithSchema(schemaInfo)
                .Create();

            messages.Add(message);
        }

        var requestBuilder = ProduceRequest.Builder
            .Stream(stream)
            .Messages(messages.ToArray());

        if (expectedVersion == ExpectedStreamVersion.NoStream)
            requestBuilder = requestBuilder.ExpectedStreamState(StreamState.Missing);
        else if (expectedVersion == ExpectedStreamVersion.Any)
            requestBuilder = requestBuilder.ExpectedStreamState(StreamState.Any);
        else
            requestBuilder = requestBuilder.ExpectedStreamRevision(StreamRevision.From(expectedVersion.Value));

        var request = requestBuilder.Create();

        var result = await Producer.Produce(request);

        return result switch {
            { Success: true }                    => new AppendEventsResult((ulong)result.Position.LogPosition.CommitPosition!, result.Position.StreamRevision),
            { Success: false, Error : not null } => throw result.Error
        };
    }

    /// <inheritdoc/>
    public async Task<StreamEvent[]> ReadEvents(
        StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken = default
    ) {
        var from = start.Value == 0
            ? LogPosition.Earliest
            : LogPosition.From((ulong?)start.Value);

        StreamEvent[] result = [];

        try {
            result = await Reader
                .ReadForwards(from, ConsumeFilter.Streams(stream), count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.Subject))
                .Select(record => new StreamEvent(
                    record.Id,
                    record.Value,
                    Metadata.FromHeaders(record.Headers),
                    record.SchemaInfo.ContentType,
                    record.Position.StreamRevision
                ))
                .ToArrayAsync(cancellationToken);
        } catch (Exception ex) when (ex is not StreamingError) {
            throw new StreamingCriticalError($"Unable to read {count} starting at {start} events from {stream}", ex);
        }

        return result.Length == 0 ? throw new StreamNotFoundError(stream) : result;
    }

    /// <inheritdoc/>
    public async Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, int count, CancellationToken cancellationToken = default) {
        StreamEvent[] result = [];

        try {
            result = await Reader
                .ReadBackwards(ConsumeFilter.Streams(stream), count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.Subject))
                .Select(record => new StreamEvent(
                    record.Id,
                    record.Value,
                    Metadata.FromHeaders(record.Headers),
                    record.SchemaInfo.ContentType,
                    record.Position.StreamRevision
                ))
                .ToArrayAsync(cancellationToken);
        } catch (Exception ex) when (ex is not StreamingError) {
            throw new StreamingCriticalError($"Unable to read {count} events backwards from {stream}", ex);
        }

        return result.Length == 0 ? throw new StreamNotFoundError(stream) : result;
    }

    /// <inheritdoc/>
    public Task TruncateStream(
        StreamName stream,
        StreamTruncatePosition truncatePosition,
        ExpectedStreamVersion expectedVersion,
        CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task DeleteStream(
        StreamName stream, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken = default
    ) => throw new NotImplementedException();

    public async ValueTask DisposeAsync() {
        await Reader.DisposeAsync();
        await Producer.DisposeAsync();
    }
}