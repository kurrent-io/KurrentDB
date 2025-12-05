// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using DotNext;
using Kurrent.Surge;
using Kurrent.Surge.Interceptors;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Interceptors;
using Kurrent.Surge.Producers.LifecycleEvents;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using Polly;
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
	    Produce(request, new ProduceResultCallback(onResult));

    public Task Produce<TState>(ProduceRequest request, OnProduceResult<TState> onResult, TState state) =>
	    Produce(request, new ProduceResultCallback<TState>(onResult, state));

    public Task Produce<TState>(ProduceRequest request, ProduceResultCallback<TState> callback) =>
	    ProduceInternal([request], callback);

    public Task Produce(ProduceRequest request, ProduceResultCallback callback) =>
	    ProduceInternal([request], callback);

    public Task Produce(ProduceRequest[] requests, OnProduceResult onResult) =>
	    ProduceInternal(requests, new ProduceResultCallback(onResult));

    public Task Produce<TState>(ProduceRequest[] requests, OnProduceResult<TState> onResult, TState state) =>
	    ProduceInternal(requests, new ProduceResultCallback<TState>(onResult, state));

    async Task ProduceInternal(ProduceRequest[] requests, IProduceResultCallback callback) {
	    Ensure.NotNull(requests);
	    Ensure.NotNull(callback);

	    if (requests.Length == 0)
		    throw new ArgumentException("No requests provided", nameof(requests));

	    foreach (var request in requests) {
		    Ensure.NotDefault(request, ProduceRequest.Empty);
		    if (request.Messages.Count == 0)
			    throw new Exception("No events in request for stream");
	    }

	    var validRequests = requests.Select(r => r.EnsureStreamIsSet(Options.DefaultStream)).ToArray();

	    foreach (var request in validRequests)
		    await Intercept(new ProduceRequestReceived(this, request));

	    Flushing.Wait();

	    await ProcessRequest(validRequests, callback);
    }

    async ValueTask ProcessRequest(ProduceRequest[] requests, IProduceResultCallback callback) {
	    var streamIds = requests.Select(r => r.Stream).ToArray();
	    var expectedVersions = requests.Select(GetExpectedVersion).ToArray();
	    var allEvents = new List<Event>();
	    var streamIndexes = new List<int>();

	    for (var i = 0; i < requests.Length; i++) {
		    var events = await requests[i].ToEvents(
			    headers => headers
				    .Set(HeaderKeys.ProducerId, ProducerId)
				    .Set(HeaderKeys.ProducerRequestId, requests[i].RequestId),
			    Serialize
		    );

		    allEvents.AddRange(events);
		    streamIndexes.AddRange(Enumerable.Repeat(i, events.Length));
	    }

	    foreach (var request in requests)
		    await Intercept(new ProduceRequestReady(this, request));

	    var state = (
		    Client,
		    Events: allEvents.ToArray(),
		    StreamIndexes: streamIndexes.ToArray(),
		    requests,
		    streamIds,
		    expectedVersions
		);

	    ProduceResult[] results;

	    try {
		    results = await ResiliencePipeline.ExecuteAsync(
			    async (ctx, token) => {
					var (position, revisions) = await ctx.Client.Writing
						.WriteEvents(ctx.streamIds, ctx.expectedVersions, ctx.Events, ctx.StreamIndexes, token)
						.ConfigureAwait(false);

					var logPosition = LogPosition.From(position.CommitPosition, position.PreparePosition);

					return ctx.requests.Select((req, i) =>
						ProduceResult.Succeeded(req,
							RecordPosition.ForStream(
								StreamId.From(ctx.streamIds[i]),
								StreamRevision.From(revisions[i].ToInt64()),
								logPosition
							))).ToArray();
				}, state);
	    } catch (StreamingError err) {
		    results = requests.Select(r => ProduceResult.Failed(r, err)).ToArray();
	    } catch (Exception ex) {
		    var streams = string.Join(", ", streamIds);
		    throw ex.ToProducerStreamingError($"[{streams}]");
	    }

	    foreach (var result in results) {
		    await Intercept(new ProduceRequestProcessed(this, result));
		    try {
			    await callback.Execute(result);
		    } catch (Exception uex) {
			    await Intercept(new ProduceRequestCallbackError(this, result, uex));
		    }
	    }

	    return;

	    static long GetExpectedVersion(ProduceRequest r) => r.ExpectedStreamRevision != StreamRevision.Unset
		    ? r.ExpectedStreamRevision.Value
		    : r.ExpectedStreamState switch {
			    StreamState.Missing => ExpectedVersion.NoStream,
			    StreamState.Exists => ExpectedVersion.StreamExists,
			    StreamState.Any => ExpectedVersion.Any,
			    _ => throw new ArgumentOutOfRangeException(nameof(r.ExpectedStreamState), r.ExpectedStreamState, null)
		    };
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
