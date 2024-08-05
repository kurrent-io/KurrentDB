// ReSharper disable CheckNamespace

using EventStore.Connect.Producers.Configuration;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Streaming;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Producers.Interceptors;
using EventStore.Streaming.Producers.LifecycleEvents;
using EventStore.Streaming.Schema.Serializers;

namespace EventStore.Connect.Producers;

[PublicAPI]
public class SystemProducer : IProducer {
    public static SystemProducerBuilder Builder => new();

    public SystemProducer(SystemProducerOptions options) {
        Options = options;
        Client  = options.Publisher;

        Serialize = (value, headers) => Options.SchemaRegistry.As<ISchemaSerializer>().Serialize(value, headers);

        Flushing = new(true);

        if (options.Logging.Enabled)
            options.Interceptors.TryAddUniqueFirst(new ProducerLogger(nameof(SystemProducer)));

        Interceptors = new(Options.Interceptors, Options.Logging.LoggerFactory.CreateLogger(nameof(SystemProducer)));

        Intercept = evt => Interceptors.Intercept(evt, CancellationToken.None);
    }

    SystemProducerOptions              Options      { get; }
    IPublisher                         Client       { get; }
    Serialize                          Serialize    { get; }
    ManualResetEventSlim               Flushing     { get; }
    InterceptorController              Interceptors { get; }
    Func<ProducerLifecycleEvent, Task> Intercept    { get; }

    public string  ProducerId       => Options.ProducerId;
    public string  ClientId         => Options.ClientId;
    public string? Stream           => Options.DefaultStream;
    public int     InFlightMessages => 0;

    public Task Produce(ProduceRequest request, OnProduceResult onResult) =>
        ProduceInternal(request, new ProduceResultCallback(onResult));

    public Task Produce<TState>(ProduceRequest request, OnProduceResult<TState> onResult, TState state) =>
        ProduceInternal(request, new ProduceResultCallback<TState>(onResult, state));

    public Task Produce<TState>(ProduceRequest request, ProduceResultCallback<TState> callback) =>
        ProduceInternal(request, callback);

    public Task Produce(ProduceRequest request, ProduceResultCallback callback) =>
        ProduceInternal(request, callback);

    public async Task ProduceInternal(ProduceRequest request, IProduceResultCallback callback) {
        Ensure.NotDefault(request, ProduceRequest.Empty);
        Ensure.NotNull(callback);

        var validRequest = request.EnsureStreamIsSet(Options.DefaultStream);

        await Intercept(new ProduceRequestReceived(this, validRequest));

        Flushing.Wait();

        var events = await validRequest
            .ToEvents(
                headers => headers
                    .Set(HeaderKeys.ProducerId, ProducerId)
                    .Set(HeaderKeys.ProducerRequestId, validRequest.RequestId),
                Serialize
            );

        await Intercept(new ProduceRequestReady(this, request));

        var expectedRevision = request.ExpectedStreamRevision != StreamRevision.Unset
            ? request.ExpectedStreamRevision.Value
            : request.ExpectedStreamState switch {
                StreamState.Missing => ExpectedVersion.NoStream,
                StreamState.Exists  => ExpectedVersion.StreamExists,
                StreamState.Any     => ExpectedVersion.Any
            };

        var result = await WriteEvents(Client, validRequest, events, expectedRevision);

        // if it is the wrong version but the stream is empty,
        // it means the stream was deleted or truncated,
        // and therefore we can retry immediately
        if (request.ExpectedStreamState == StreamState.Missing && result.Error is ExpectedStreamRevisionError revisionError) {
            result = await Client
                .ReadStreamLastEvent(validRequest.Stream)
                .Then(async re => re is null || re == ResolvedEvent.EmptyEvent
                    ? await WriteEvents(Client, validRequest, events, revisionError.ActualStreamRevision)
                    : result);
        }

        await Intercept(new ProduceRequestProcessed(this, result));

        try {
            await callback.Execute(result);
        } catch (Exception uex) {
            await Intercept(new ProduceRequestCallbackError(this, result, uex));
        }

        return;

        static async Task<ProduceResult> WriteEvents(IPublisher client, ProduceRequest request, Event[] events, long expectedRevision) {
            try {
                var (position, streamRevision) = await client.WriteEvents(
                    request.Stream,
                    events,
                    expectedRevision
                );

                var recordPosition = RecordPosition.ForStream(
                    StreamId.From(request.Stream),
                    StreamRevision.From(streamRevision.ToInt64()),
                    LogPosition.From(position.CommitPosition, position.PreparePosition)
                );

                return ProduceResult.Succeeded(request, recordPosition);
            }
            catch (Exception ex) {
                StreamingError error = ex switch {
                    ReadResponseException.Timeout        => new RequestTimeoutError(request.Stream, ex.Message),
                    ReadResponseException.StreamNotFound => new StreamNotFoundError(request.Stream),
                    ReadResponseException.StreamDeleted  => new StreamDeletedError(request.Stream),
                    ReadResponseException.AccessDenied   => new StreamAccessDeniedError(request.Stream),
                    ReadResponseException.WrongExpectedRevision wex => new ExpectedStreamRevisionError(
                        request.Stream,
                        StreamRevision.From(wex.ExpectedStreamRevision.ToInt64()),
                        StreamRevision.From(wex.ActualStreamRevision.ToInt64())
                    ),
                    ReadResponseException.NotHandled.ServerNotReady => new ServerNotReadyError(),
                    ReadResponseException.NotHandled.ServerBusy     => new ServerTooBusyError(),
                    ReadResponseException.NotHandled.LeaderInfo li  => new ServerNotLeaderError(li.Host, li.Port),
                    ReadResponseException.NotHandled.NoLeaderInfo   => new ServerNotLeaderError(),
                    _                                               => new StreamingCriticalError(ex.Message, ex)
                };

                return ProduceResult.Failed(request, error);
            }
        }
    }

    public async Task<(int Flushed, int Inflight)> Flush(CancellationToken cancellationToken = default) {
        try {
            Flushing.Reset();
            await Intercept(new ProducerFlushed(this, 0, 0));
            return (0, 0);
        } finally {
            Flushing.Set();
        }
    }

    public virtual async ValueTask DisposeAsync() {
        try {
            await Flush();

            await Intercept(new ProducerStopped(this));
        } catch (Exception ex) {
            await Intercept(new ProducerStopped(this, ex)); //not sure about this...
            throw;
        } finally {
            await Interceptors.DisposeAsync();
        }
    }
}