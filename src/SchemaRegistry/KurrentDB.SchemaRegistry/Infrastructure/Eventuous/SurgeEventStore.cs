using Eventuous;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Readers;
using Kurrent.Surge.Schema;
using StreamRevision = Kurrent.Surge.StreamRevision;
using StreamState = Kurrent.Surge.StreamState;

namespace KurrentDB.SchemaRegistry.Infrastructure.Eventuous;

[UsedImplicitly]
public class SurgeEventStore(IReader reader, IProducer producer, IManager manager) : IEventStore, IAsyncDisposable {
    IReader   Reader   { get; } = reader;
    IProducer Producer { get; } = producer;
    IManager  Manager  { get; } = manager;

    /// <inheritdoc/>
    public async Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken = default) {
        try {
            return await Manager.StreamExists(StreamId.From(stream.ToString()), cancellationToken);
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
                .WithSchemaType(SchemaDataFormat.Json)
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
            { Success: true } => new(
                (ulong)result.Position.LogPosition.CommitPosition!,
                result.Position.StreamRevision
            ),
            { Success: false } => throw result.Error ?? new StreamingCriticalError($"Unable to append events to {stream} stream"),
        };
    }

    /// <inheritdoc/>
    public async Task<StreamEvent[]> ReadEvents(
        StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken = default
    ) {
        var from = start.Value == 0
            ? LogPosition.Earliest
            : LogPosition.From((ulong?)start.Value);

        StreamEvent[] result;

        var filter = ConsumeFilter.FromStreamId(StreamId.From(stream));

        try {
            result = await Reader
                .ReadForwards(from, filter, count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.SchemaName)) // what?
                .Select(record => new StreamEvent(
                            record.Id,
                            record.Value,
                            Metadata.FromHeaders(record.Headers),
                            record.SchemaInfo.ContentType,
                            record.Position.StreamRevision
                        )
                )
                .ToArrayAsync(cancellationToken);
        } catch (Exception ex) {
            if (ex is StreamNotFoundError) throw new StreamNotFound(stream); //eventuous ffs

            // TODO SS: must validate what exceptions are actually thrown when reading events
            StreamingError error = ex switch {
                TimeoutException      => new RequestTimeoutError(stream, ex.Message),
                StreamNotFound        => new StreamNotFoundError(stream),
                DeleteStreamException => new StreamDeletedError(stream),

                not null when ex.GetType().Name.Contains("AccessDenied")   => new StreamAccessDeniedError(stream),
                not null when ex.GetType().Name.Contains("ServerNotReady") => new ServerNotReadyError(),
                not null when ex.GetType().Name.Contains("ServerBusy")     => new ServerTooBusyError(),
                not null when ex.GetType().Name.Contains("LeaderInfo")     => new ServerNotLeaderError(),
                not null when ex.GetType().Name.Contains("NoLeaderInfo")   => new ServerNotLeaderError(),

                _ => new StreamingCriticalError($"Unable to read {count} starting at {start} events from {stream}", ex)
            };

            throw error;
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<StreamEvent[]> ReadEventsBackwards(
        StreamName stream, StreamReadPosition start, int count,
        CancellationToken cancellationToken = default
    ) {
        StreamEvent[] result;

        try {
            result = await Reader
                .ReadBackwards(ConsumeFilter.FromStreamId(stream.ToString()), count, cancellationToken)
                .Where(x => !"$".StartsWith(x.SchemaInfo.SchemaName))
                .Select(record => new StreamEvent(
                            record.Id,
                            record.Value,
                            Metadata.FromHeaders(record.Headers),
                            record.SchemaInfo.ContentType,
                            record.Position.StreamRevision
                        )
                )
                .ToArrayAsync(cancellationToken);
        } catch (Exception ex) {
            // TODO SS: must validate what exceptions are actually thrown when reading events
            StreamingError error = ex switch {
                TimeoutException      => new RequestTimeoutError(stream, ex.Message),
                StreamNotFound        => new StreamNotFoundError(stream),
                DeleteStreamException => new StreamDeletedError(stream),

                not null when ex.GetType().Name.Contains("AccessDenied")   => new StreamAccessDeniedError(stream),
                not null when ex.GetType().Name.Contains("ServerNotReady") => new ServerNotReadyError(),
                not null when ex.GetType().Name.Contains("ServerBusy")     => new ServerTooBusyError(),
                not null when ex.GetType().Name.Contains("LeaderInfo")     => new ServerNotLeaderError(),
                not null when ex.GetType().Name.Contains("NoLeaderInfo")   => new ServerNotLeaderError(),

                _ => new StreamingCriticalError($"Unable to read {count} events backwards from {stream}", ex)
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