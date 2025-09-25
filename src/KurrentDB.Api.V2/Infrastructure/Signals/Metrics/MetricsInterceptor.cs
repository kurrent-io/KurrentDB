// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Grpc.Core.Interceptors;
using KurrentDB.Api.Streams;
using KurrentDB.Common.Configuration;
using KurrentDB.Core;
using KurrentDB.Core.Metrics;

namespace KurrentDB.Api.Infrastructure.Signals.Metrics;

public class MetricsInterceptor(ApiV2Trackers trackers) : Interceptor {
    IDurationTracker AppendDuration { get; } = trackers[MetricsConfiguration.ApiV2Method.StreamAppendSession];

	public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation) {
        if (context.Method != $"/kurrentdb.api.v2.Streams/{nameof(StreamsService.AppendSession)}")
            return base.UnaryServerHandler(request, context, continuation);

        return AppendDuration.Record(static method => method, continuation(request, context));
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation) =>
        AppendDuration.Record(static method => method, continuation(requestStream, context));

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation) =>
        AppendDuration.Record(static method => method, continuation(request, responseStream, context));

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation) =>
        AppendDuration.Record(static method => method, continuation(requestStream, responseStream, context));
}
