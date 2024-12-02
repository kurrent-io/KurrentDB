using EventStore.Connect.Producers;
using EventStore.Connect.Readers;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using EventStore.Streaming.Schema;
using EventStore.Toolkit;
using Eventuous;

namespace EventStore.Connectors.Eventuous;

[UsedImplicitly]
public class SystemEventStore(SystemReader reader, SystemProducer producer) : IEventStore, IAsyncDisposable {
    SystemReader   Reader   { get; } = reader;
    SystemProducer Producer { get; } = producer;

    /// <inheritdoc/>
    public async Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken = default) {
        try {
            var isDeleted = await Reader
                .ReadLastStreamRecord(SystemStreams.MetastreamOf(stream), cancellationToken)
                .Then(record => {
                    if (record == EventStoreRecord.None)
                        return false;

                    var metadata  = StreamMetadata.FromJsonBytes(record.Data);
                    var isDeleted = metadata.TruncateBefore == long.MaxValue;
                    return isDeleted;
                });

            if (isDeleted)
                return false;

            return await Reader
                .ReadLastStreamRecord(stream.ToString(), cancellationToken)
                .Then(record => record != EventStoreRecord.None);
        }
        catch (Exception ex) when (ex is not StreamingError) {
            throw new StreamingCriticalError($"Unable to check if stream {stream} exists", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<AppendEventsResult> AppendEvents(
        StreamName stream,
        ExpectedStreamVersion expectedVersion,
        IReadOnlyCollection<NewStreamEvent> events,
        CancellationToken cancellationToken = default
    ) {
        List<Message> messages = [];

        foreach (var evt in events) {
            ArgumentNullException.ThrowIfNull(evt.Payload, nameof(evt.Payload)); // must do it better

            var headers = new Headers(evt.Metadata.ToHeaders());

            var message = Message.Builder
                .RecordId(evt.Id)
                .Value(evt.Payload)
                .Headers(headers)
                // TODO SS: schema definition type should come from the eventuous event headers to support any schema type (not important for now)
                .WithSchemaType(SchemaDefinitionType.Json)
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
    public async Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken = default) {
        var from = start.Value == 0
            ? LogPosition.Earliest
            : LogPosition.From((ulong?)start.Value);

        StreamEvent[] result;

        var filter = ConsumeFilter.FromStreamId(StreamId.From(stream));

        try {
            result = await Reader
                .ReadForwards(from, filter, count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.Subject)) // what?
                .Select(record => new StreamEvent(
                    record.Id,
                    record.Value,
                    Metadata.FromHeaders(record.Headers),
                    record.SchemaInfo.ContentType,
                    record.Position.StreamRevision
                ))
                .ToArrayAsync(cancellationToken);
        }
        catch (Exception ex) {
            if (ex is ReadResponseException.StreamNotFound) throw new StreamNotFound(stream); //eventuous ffs

            // TODO SS: must validate what exceptions are actually thrown when reading events
            StreamingError error = ex switch {
                ReadResponseException.Timeout        => new RequestTimeoutError(stream, ex.Message),
                ReadResponseException.StreamNotFound => new StreamNotFoundError(stream),
                ReadResponseException.StreamDeleted  => new StreamDeletedError(stream),
                ReadResponseException.AccessDenied   => new StreamAccessDeniedError(stream),

                ReadResponseException.NotHandled.ServerNotReady => new ServerNotReadyError(),
                ReadResponseException.NotHandled.ServerBusy     => new ServerTooBusyError(),
                ReadResponseException.NotHandled.LeaderInfo li  => new ServerNotLeaderError(li.Host, li.Port),
                ReadResponseException.NotHandled.NoLeaderInfo   => new ServerNotLeaderError(),
                _                                               => new StreamingCriticalError($"Unable to read {count} starting at {start} events from {stream}", ex)
            };

            throw error;
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken = default) {
        StreamEvent[] result;

        try {
            result = await Reader
                .ReadBackwards(ConsumeFilter.FromStreamId(stream.ToString()), count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.Subject))
                .Select(record => new StreamEvent(
                    record.Id,
                    record.Value,
                    Metadata.FromHeaders(record.Headers),
                    record.SchemaInfo.ContentType,
                    record.Position.StreamRevision
                ))
                .ToArrayAsync(cancellationToken);
        }
        catch (Exception ex) {
            // TODO SS: must validate what exceptions are actually thrown when reading events
            StreamingError error = ex switch {
                ReadResponseException.Timeout        => new RequestTimeoutError(stream, ex.Message),
                ReadResponseException.StreamNotFound => new StreamNotFoundError(stream),
                ReadResponseException.StreamDeleted  => new StreamDeletedError(stream),
                ReadResponseException.AccessDenied   => new StreamAccessDeniedError(stream),

                ReadResponseException.NotHandled.ServerNotReady => new ServerNotReadyError(),
                ReadResponseException.NotHandled.ServerBusy     => new ServerTooBusyError(),
                ReadResponseException.NotHandled.LeaderInfo li  => new ServerNotLeaderError(li.Host, li.Port),
                ReadResponseException.NotHandled.NoLeaderInfo   => new ServerNotLeaderError(),
                _                                               => new StreamingCriticalError($"Unable to read {count} events backwards from {stream}", ex)
            };

            throw error;
        }

        return result;
    }

    /// <inheritdoc/>
    public Task TruncateStream(
        StreamName stream,
        StreamTruncatePosition truncatePosition,
        ExpectedStreamVersion expectedVersion,
        CancellationToken cancellationToken
    ) => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task DeleteStream(
        StreamName stream,
        ExpectedStreamVersion expectedVersion,
        CancellationToken cancellationToken
    ) => throw new NotImplementedException();

    public async ValueTask DisposeAsync() {
        await Reader.DisposeAsync();
        await Producer.DisposeAsync();
    }
}