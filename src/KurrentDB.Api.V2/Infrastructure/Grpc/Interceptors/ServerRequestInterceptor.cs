// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;

public abstract class ServerRequestAsyncInterceptor : Interceptor {
    /// <summary>
    /// Intercepts the incoming request message.
    /// </summary>
    protected abstract ValueTask<TRequest> InterceptRequestAsync<TRequest>(TRequest request, ServerCallContext context) where TRequest : class;

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation
    ) {
        var intercept = InterceptRequestAsync(request, context);

        return intercept.IsCompletedSuccessfully
            ? continuation(intercept.Result, context)
            : ContinueAsync(intercept, context, continuation);

        static async Task<TResponse> ContinueAsync(ValueTask<TRequest> action, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation) {
            var result = await action;
            return await continuation(result, context);
        }
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation
    ) => continuation(new InterceptingStreamReader<TRequest>(requestStream, context, InterceptRequestAsync), context);

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation
    ) {
        var intercept = InterceptRequestAsync(request, context);
        return intercept.IsCompletedSuccessfully
            ? continuation(intercept.Result, responseStream, context)
            : ContinueAsync(intercept, responseStream, context, continuation);

        static async Task ContinueAsync(ValueTask<TRequest> action, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation) {
            var result = await action;
            await continuation(result, responseStream, context);
        }
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation) =>
        continuation(new InterceptingStreamReader<TRequest>(requestStream, context, InterceptRequestAsync), responseStream, context);
}

public abstract class ServerRequestInterceptor : ServerRequestAsyncInterceptor {
    protected override ValueTask<TRequest> InterceptRequestAsync<TRequest>(TRequest request, ServerCallContext context) where TRequest : class =>
        ValueTask.FromResult(InterceptRequest(request, context));

    protected abstract TRequest InterceptRequest<TRequest>(TRequest request, ServerCallContext context) where TRequest : class;
}
