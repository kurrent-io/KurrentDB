// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Grpc.Core;

using static System.Runtime.CompilerServices.MethodImplOptions;

namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;

public delegate ValueTask<T> InterceptRequestAsync<T>(T request, ServerCallContext context);
public delegate T            InterceptRequest<T>(T request, ServerCallContext context);
public delegate ValueTask<T> InterceptRequestAsync<T, in TState>(TState state, T request, ServerCallContext context);
public delegate T            InterceptRequest<T, in TState>(TState state, T request, ServerCallContext context);

public sealed class InterceptingStreamReader<T>(IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T> intercept) : IAsyncStreamReader<T> {
    public T Current { get; private set; } = default(T)!;

    [MethodImpl(AggressiveInlining)]
    public async Task<bool> MoveNext(CancellationToken cancellationToken) {
        if (!await requestStream.MoveNext(cancellationToken)) {
            Current = requestStream.Current;
            return false;
        }

        var action = intercept(requestStream.Current, context);

        Current = action.IsCompletedSuccessfully
            ? action.Result : await action;

        return true;
    }
}

public sealed class InterceptingStreamReader<T, TState>(IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T, TState> intercept, TState state) : IAsyncStreamReader<T> {
    public T Current { get; private set; } = default(T)!;

    [MethodImpl(AggressiveInlining)]
    public async Task<bool> MoveNext(CancellationToken cancellationToken) {
        if (!await requestStream.MoveNext(cancellationToken)) {
            Current = requestStream.Current;
            return false;
        }

        var action = intercept(state, requestStream.Current, context);

        Current = action.IsCompletedSuccessfully
            ? action.Result : await action;

        return true;
    }
}

public static class AsyncStreamReaderExtensions {
    public static IAsyncStreamReader<T> Intercept<T>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T> intercept) =>
        new InterceptingStreamReader<T>(requestStream, context, intercept);

    public static IAsyncStreamReader<T> Intercept<T>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequest<T> intercept) =>
        new InterceptingStreamReader<T>(requestStream, context, new SyncToAsyncWrapper<T>(intercept).InvokeAsync);

    public static IAsyncStreamReader<T> Intercept<T, TState>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T, TState> intercept, TState state) =>
        new InterceptingStreamReader<T, TState>(requestStream, context, intercept, state);

    public static IAsyncStreamReader<T> Intercept<T, TState>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequest<T, TState> intercept, TState state) =>
        new InterceptingStreamReader<T, TState>(requestStream, context, new SyncToAsyncWrapper<T, TState>(intercept).InvokeAsync, state);

    sealed class SyncToAsyncWrapper<T>(InterceptRequest<T> intercept) {
        [MethodImpl(AggressiveInlining)]
        public ValueTask<T> InvokeAsync(T request, ServerCallContext context) =>
            new(intercept(request, context));
    }

    sealed class SyncToAsyncWrapper<T, TState>(InterceptRequest<T, TState> intercept) {
        [MethodImpl(AggressiveInlining)]
        public ValueTask<T> InvokeAsync(TState state, T request, ServerCallContext context) =>
            new(intercept(state, request, context));
    }
}
