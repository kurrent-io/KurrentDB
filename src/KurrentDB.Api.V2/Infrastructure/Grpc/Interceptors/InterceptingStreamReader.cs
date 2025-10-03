using Grpc.Core;

namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;

public delegate ValueTask<T> InterceptRequestAsync<T>(T request, ServerCallContext context);
public delegate T            InterceptRequest<T>(T request, ServerCallContext context);
public delegate ValueTask<T> InterceptRequestAsync<T, in TState>(TState state, T request, ServerCallContext context);
public delegate T            InterceptRequest<T, in TState>(TState state, T request, ServerCallContext context);

public sealed class InterceptingStreamReader<T>(IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T> intercept) : IAsyncStreamReader<T> {
    public T Current { get; private set; } = default(T)!;

    public async Task<bool> MoveNext(CancellationToken cancellationToken) {
        var hasNext = await requestStream.MoveNext(cancellationToken);
        if (hasNext) {
            var action = intercept(requestStream.Current, context);
            Current = action.IsCompletedSuccessfully
                ? action.Result : await action;
        }
        else
            Current = requestStream.Current;

        return hasNext;
    }
}

public sealed class InterceptingStreamReader<T, TState>(IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T, TState> intercept, TState state) : IAsyncStreamReader<T> {
    public T Current { get; private set; } = default(T)!;

    public async Task<bool> MoveNext(CancellationToken cancellationToken) {
        var hasNext = await requestStream.MoveNext(cancellationToken);
        if (hasNext) {
            var action = intercept(state, requestStream.Current, context);
            Current = action.IsCompletedSuccessfully
                ? action.Result : await action;
        }
        else
            Current = requestStream.Current;

        return hasNext;
    }
}

public static class AsyncStreamReaderExtensions {
    public static IAsyncStreamReader<T> Intercept<T>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T> intercept) =>
        new InterceptingStreamReader<T>(requestStream, context, intercept);

    public static IAsyncStreamReader<T> Intercept<T>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequest<T> intercept) =>
        new InterceptingStreamReader<T>(requestStream, context, (r, x) => ValueTask.FromResult(intercept(r, x)));

    public static IAsyncStreamReader<T> Intercept<T, TState>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequestAsync<T, TState> intercept, TState state) =>
        new InterceptingStreamReader<T, TState>(requestStream, context, intercept, state);

    public static IAsyncStreamReader<T> Intercept<T, TState>(this IAsyncStreamReader<T> requestStream, ServerCallContext context, InterceptRequest<T, TState> intercept, TState state) =>
        new InterceptingStreamReader<T, TState>(requestStream, context, (s, r, x) => ValueTask.FromResult(intercept(s, r, x)), state);
}
