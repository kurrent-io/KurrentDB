// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;

/// <summary>
/// A server interceptor that executes custom logic on each and every incoming request message.
/// It supports unary, client-streaming, server-streaming, and duplex-streaming RPCs.
/// This is useful for scenarios such as logging, validation, or authorization on a per-message basis.
/// </summary>
public abstract class OnRequestInterceptor : Interceptor {
    /// <summary>
    /// Intercepts the incoming request message.
    /// </summary>
    protected abstract ValueTask Intercept<TRequest>(TRequest request, ServerCallContext context) where TRequest : class;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation
    ) {
        await Intercept(request, context);
        return await continuation(request, context);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation
    ) {
        var pipeline = requestStream
            .ReadAllAsync()
            .Do(async (req, _) => await Intercept(req, context))
            .ToAsyncStreamReader();

        return continuation(pipeline, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation
    ) {
        await Intercept(request, context);
        await continuation(request, responseStream, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var pipeline = requestStream
            .ReadAllAsync()
            .Do(async (req, _) => await Intercept(req, context))
            .ToAsyncStreamReader();

        return continuation(pipeline, responseStream, context);
    }
}

/// <summary>
/// Adapter to convert IAsyncEnumerable back to IAsyncStreamReader
/// </summary>
public class AsyncEnumerableStreamReader<T>(IAsyncEnumerable<T> requestStream, CancellationToken cancellationToken = default) : IAsyncStreamReader<T> {
    readonly IAsyncEnumerator<T> _enumerator = requestStream.GetAsyncEnumerator(cancellationToken);

    public T Current => _enumerator.Current;

    public async Task<bool> MoveNext(CancellationToken cancellationToken) =>
        await _enumerator.MoveNextAsync(cancellationToken);
}

public static class AsyncEnumerableStreamReaderExtensions {
    public static IAsyncStreamReader<T> ToAsyncStreamReader<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default) =>
        new AsyncEnumerableStreamReader<T>(source, cancellationToken);
}

public class StreamReaderInterceptor<T, TState>(IAsyncStreamReader<T> requestStream,  TState state, InterceptRequest<T, TState> intercept) : IAsyncStreamReader<T> {
    public T Current { get; private set; } = default(T)!;

    public async Task<bool> MoveNext(CancellationToken cancellationToken) {
        var hasNext = await requestStream.MoveNext(cancellationToken);

        if (hasNext)
            await intercept(requestStream.Current, state);

        Current = requestStream.Current;

        return hasNext;
    }
}

/// <summary>
/// A delegate that defines a method signature for intercepting gRPC requests.
/// </summary>
public delegate ValueTask InterceptRequest<in TRequest>(TRequest request);

/// <summary>
/// A delegate that defines a method signature for intercepting gRPC requests with additional state.
/// </summary>
public delegate ValueTask InterceptRequest<in TRequest, in TState>(TRequest request, TState state);
