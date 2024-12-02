// ReSharper disable CheckNamespace

using DotNext;
using EventStore.Connect.Producers.Configuration;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Producers.Interceptors;
using EventStore.Streaming.Producers.LifecycleEvents;
using EventStore.Streaming.Schema.Serializers;
using EventStore.Toolkit;
using Polly;

namespace EventStore.Connect.Producers;

[PublicAPI]
public class SystemProducer : IProducer {
    public static SystemProducerBuilder Builder => new();

    public SystemProducer(SystemProducerOptions options) {
        Options = string.IsNullOrWhiteSpace(options.Logging.LogName)
            ? options with { Logging = options.Logging with { LogName = GetType().FullName! } }
            : options;

        var logger = Options.Logging.LoggerFactory.CreateLogger(GetType().FullName!);

        Client  = options.Publisher;

        Serialize = (value, headers) => Options.SchemaRegistry.As<ISchemaSerializer>().Serialize(value, headers);

        Flushing = new(true);

        if (options.Logging.Enabled)
            options.Interceptors.TryAddUniqueFirst(new ProducerLogger());

        Interceptors = new(Options.Interceptors, logger);

        Intercept = evt => Interceptors.Intercept(evt, CancellationToken.None);

        ResiliencePipeline = Options.ResiliencePipelineBuilder
            .With(x => x.InstanceName = "SystemProducerPipeline")
            .Build();
    }

    SystemProducerOptions              Options            { get; }
    IPublisher                         Client             { get; }
    Serialize                          Serialize          { get; }
    ManualResetEventSlim               Flushing           { get; }
    InterceptorController              Interceptors       { get; }
    Func<ProducerLifecycleEvent, Task> Intercept          { get; }
    ResiliencePipeline                 ResiliencePipeline { get; }

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

    async Task ProduceInternal(ProduceRequest request, IProduceResultCallback callback) {
        Ensure.NotDefault(request, ProduceRequest.Empty);
        Ensure.NotNull(callback);

        if (request.Messages.Count == 0)
            throw new Exception("No events received");

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

        var result = await WriteEvents(Client, validRequest, events, expectedRevision, ResiliencePipeline);

        await Intercept(new ProduceRequestProcessed(this, result));

        try {
            await callback.Execute(result);
        } catch (Exception uex) {
            await Intercept(new ProduceRequestCallbackError(this, result, uex));
        }

        return;

        static async Task<ProduceResult> WriteEvents(IPublisher client, ProduceRequest request, Event[] events, long expectedRevision, ResiliencePipeline resiliencePipeline) {
            var state = (Client: client, Request: request, Events: events, ExpectedRevision: expectedRevision);

            try {
                return await resiliencePipeline.ExecuteAsync(
                    static async (state, token) => {
                        var result = await WriteEvents(state.Client, state.Request, state.Events, state.ExpectedRevision, token);

                        // If it is the wrong version but the stream is empty,
                        // it means the stream was deleted or truncated.
                        // Therefore, we can retry immediately
                        if (state.Request.ExpectedStreamState == StreamState.Missing && result.Error is ExpectedStreamRevisionError revisionError) {
                            result = await state.Client
                                .ReadStreamLastEvent(state.Request.Stream, CancellationToken.None)
                                .Then(async re => re is null || re == ResolvedEvent.EmptyEvent
                                    ? await WriteEvents(state.Client, state.Request, state.Events, revisionError.ActualStreamRevision, token)
                                    : result);
                        }

                        return result.Error is null ? result : throw result.Error;
                    }, state
                );
            }
            // we have to recreate the error result... must rethink this
            catch (StreamingError err) {
                return ProduceResult.Failed(request, err);
            }

            static async Task<ProduceResult> WriteEvents(IPublisher client, ProduceRequest request, Event[] events, long expectedRevision, CancellationToken cancellationToken) {
                try {
                    var (position, streamRevision) = await client.WriteEvents(request.Stream, events, expectedRevision, cancellationToken);

                    var recordPosition = RecordPosition.ForStream(
                        StreamId.From(request.Stream),
                        StreamRevision.From(streamRevision.ToInt64()),
                        LogPosition.From(position.CommitPosition, position.PreparePosition)
                    );

                    return ProduceResult.Succeeded(request, recordPosition);
                }
                catch (Exception ex) {
                    return ProduceResult.Failed(request, ex.ToProducerStreamingError(request.Stream));
                }
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