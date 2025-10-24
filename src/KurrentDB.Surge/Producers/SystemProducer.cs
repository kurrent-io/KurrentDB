// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Google.Protobuf.Collections;
using Grpc.Core;
using Kurrent.Surge;
using Kurrent.Surge.Interceptors;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Interceptors;
using Kurrent.Surge.Producers.LifecycleEvents;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Protocol.V2.Streams;
using Polly;
using Serilog;
using StreamRevision = Kurrent.Surge.StreamRevision;

namespace KurrentDB.Surge.Producers;

[PublicAPI]
public class SystemProducer : IProducer {
    public static SystemProducerBuilder Builder => new();

    public SystemProducer(SystemProducerOptions options) {
        Options = string.IsNullOrWhiteSpace(options.Logging.LogName)
            ? options with { Logging = options.Logging with { LogName = GetType().FullName! } }
            : options;

        var logger = Options.Logging.LoggerFactory.CreateLogger(GetType().FullName!);

        Client  = options.Client;
        StreamsClient = options.StreamsClient;

        Serialize = (value, headers) => Options.SchemaRegistry.As<ISchemaSerializer>().Serialize(value, headers);

        Flushing = new(true);

        if (options.Logging.Enabled)
            options.Interceptors.TryAddUniqueFirst(new ProducerLogger());

        Options.Interceptors.TryAddUniqueFirst(new ProducerMetrics());

        Interceptors = new(Options.Interceptors, logger);

        Intercept = evt => Interceptors.Intercept(evt, CancellationToken.None);

        ResiliencePipeline = Options.ResiliencePipelineBuilder
            .With(x => x.InstanceName = "SystemProducerPipeline")
            .Build();
    }

    SystemProducerOptions Options { get; }
    ISystemClient Client { get; }
    StreamsService.StreamsServiceClient? StreamsClient { get; }
    Serialize Serialize { get; }
    ManualResetEventSlim Flushing { get; }
    InterceptorController Interceptors { get; }
    Func<ProducerLifecycleEvent, Task> Intercept { get; }
    ResiliencePipeline ResiliencePipeline { get; }

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

        var expectedRevision = request.ExpectedStreamRevision != StreamRevision.Unset
            ? request.ExpectedStreamRevision.Value
            : request.ExpectedStreamState switch {
                StreamState.Missing => ExpectedVersion.NoStream,
                StreamState.Exists  => ExpectedVersion.StreamExists,
                StreamState.Any     => ExpectedVersion.Any,
                _ => throw new ArgumentOutOfRangeException(nameof(request.ExpectedStreamState), request.ExpectedStreamState, null)
            };

        ProduceResult result;

        if (StreamsClient is not null) {
	        var records = await validRequest.ToAppendRecords(
		        headers => headers
			        .Set(HeaderKeys.ProducerId, ProducerId)
			        .Set(HeaderKeys.ProducerRequestId, validRequest.RequestId),
		        Serialize
	        );

	        await Intercept(new ProduceRequestReady(this, request));

	        result = await WriteEvents(StreamsClient, validRequest, records, expectedRevision, ResiliencePipeline);
        } else {
	        var events = await validRequest.ToEvents(
		        headers => headers
			        .Set(HeaderKeys.ProducerId, ProducerId)
			        .Set(HeaderKeys.ProducerRequestId, validRequest.RequestId),
		        Serialize
	        );

	        await Intercept(new ProduceRequestReady(this, request));

	        result = await WriteEventsLegacy(Client, validRequest, events, expectedRevision, ResiliencePipeline); // required until system reader are refactored to use streams client
        }

        await Intercept(new ProduceRequestProcessed(this, result));

        try {
            await callback.Execute(result);
        } catch (Exception uex) {
            await Intercept(new ProduceRequestCallbackError(this, result, uex));
        }

        return;

        static async Task<ProduceResult> WriteEvents(
            StreamsService.StreamsServiceClient streamsClient,
            ProduceRequest request,
            RepeatedField<AppendRecord> records,
            long expectedRevision,
            ResiliencePipeline resiliencePipeline) {

            var state = (StreamsClient: streamsClient, Request: request, Records: records, ExpectedRevision: expectedRevision);

            try {
                return await resiliencePipeline.ExecuteAsync(
                    static async (state, token) => {
                        var result = await WriteEvents(
                            state.StreamsClient,
                            state.Request,
                            state.Records,
                            state.ExpectedRevision,
                            token);

                        return result.Error is null ? result : throw result.Error;
                    }, state
                );
            }
            catch (StreamingError err) {
                return ProduceResult.Failed(request, err);
            }

            static async Task<ProduceResult> WriteEvents(StreamsService.StreamsServiceClient streamsClient,
	            ProduceRequest request, RepeatedField<AppendRecord> records, long expectedRevision, CancellationToken cancellationToken
			) {
                try {
                    using var session = streamsClient.AppendSession(cancellationToken: cancellationToken);

                    var appendRequest = new AppendRequest { Stream = request.Stream };

                    if (expectedRevision is not ExpectedVersion.Any)
	                    appendRequest.ExpectedRevision = expectedRevision;

                    appendRequest.Records.AddRange(records);

                    await session.RequestStream.WriteAsync(appendRequest);
                    await session.RequestStream.CompleteAsync();

                    var response = await session.ResponseAsync;

                    var recordPosition = RecordPosition.ForStream(
                        StreamId.From(request.Stream),
                        StreamRevision.From(response.Output.First().StreamRevision),
                        LogPosition.From(response.Position, response.Position)
                    );

                    return ProduceResult.Succeeded(request, recordPosition);
                }
                catch (RpcException rpcEx) {
                    return ProduceResult.Failed(request, MapRpcExceptionToStreamingError(rpcEx, request.Stream));
                }
                catch (Exception ex) {
                    return ProduceResult.Failed(request, ex.ToProducerStreamingError(request.Stream));
                }
            }

            static StreamingError MapRpcExceptionToStreamingError(RpcException rpcEx, string stream) {
                return rpcEx.StatusCode switch {
                    StatusCode.DeadlineExceeded => new RequestTimeoutError(stream, "Deadline exceeded"),
                    StatusCode.PermissionDenied => new StreamAccessDeniedError(stream),
                    StatusCode.NotFound => new StreamDeletedError(stream),
                    StatusCode.FailedPrecondition when rpcEx.Status.Detail.Contains("WrongExpectedVersion")
                        => new ExpectedStreamRevisionError(stream, StreamRevision.From(-1), StreamRevision.From(-1)),
                    _ => new StreamingCriticalError(rpcEx.Status.Detail, rpcEx)
                };
            }
        }

        static async Task<ProduceResult> WriteEventsLegacy(
            ISystemClient client,
            ProduceRequest request,
            Event[] events,
            long expectedRevision,
            ResiliencePipeline resiliencePipeline) {

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
                                .Reading
                                .ReadStreamLastEvent(state.Request.Stream, CancellationToken.None)
                                .Then(async re => re is null || re == ResolvedEvent.EmptyEvent
                                    ? await WriteEvents(state.Client, state.Request, state.Events, revisionError.ActualStreamRevision, token)
                                    : result);
                        }

                        return result.Error is null ? result : throw result.Error;
                    }, state
                );
            }
            catch (StreamingError err) {
                return ProduceResult.Failed(request, err);
            }

            static async Task<ProduceResult> WriteEvents(
                ISystemClient client,
                ProduceRequest request,
                Event[] events,
                long expectedRevision,
                CancellationToken cancellationToken) {

                try {
                    var (position, streamRevision) = await client.Writing.WriteEvents(request.Stream, events, expectedRevision, cancellationToken);

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
