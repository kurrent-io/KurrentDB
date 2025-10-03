// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using DotNext.Threading.Tasks;
// using Grpc.Core;
// using Grpc.Core.Interceptors;
//
// namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;
//
// /// <summary>
// /// A server interceptor that executes custom logic on each and every incoming request message.
// /// It supports unary, client-streaming, server-streaming, and duplex-streaming RPCs.
// /// This is useful for scenarios such as logging, validation, or authorization on a per-message basis.
// /// </summary>
// public abstract class OnRequestInterceptor : Interceptor {
//     /// <summary>
//     /// Intercepts the incoming request message.
//     /// </summary>
//     protected abstract ValueTask Intercept<TRequest>(TRequest request, ServerCallContext context) where TRequest : class;
//
//     public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
//         TRequest request,
//         ServerCallContext context,
//         UnaryServerMethod<TRequest, TResponse> continuation
//     ) {
//         await Intercept(request, context);
//         return await continuation(request, context);
//     }
//
//     public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
//         IAsyncStreamReader<TRequest> requestStream,
//         ServerCallContext context,
//         ClientStreamingServerMethod<TRequest, TResponse> continuation
//     ) {
//         var pipeline = requestStream
//             .ReadAllAsync()
//             .Do(async (req, _) => await Intercept(req, context))
//             .ToAsyncStreamReader();
//
//         return continuation(pipeline, context);
//     }
//
//     public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
//         TRequest request,
//         IServerStreamWriter<TResponse> responseStream,
//         ServerCallContext context,
//         ServerStreamingServerMethod<TRequest, TResponse> continuation
//     ) {
//         await Intercept(request, context);
//         await continuation(request, responseStream, context);
//     }
//
//     public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
//         IAsyncStreamReader<TRequest> requestStream,
//         IServerStreamWriter<TResponse> responseStream,
//         ServerCallContext context,
//         DuplexStreamingServerMethod<TRequest, TResponse> continuation)
//     {
//         var pipeline = requestStream
//             .ReadAllAsync()
//             .Do(async (req, _) => await Intercept(req, context))
//             .ToAsyncStreamReader();
//
//         return continuation(pipeline, responseStream, context);
//     }
// }
//
// /// <summary>
// /// Adapter to convert IAsyncEnumerable back to IAsyncStreamReader
// /// </summary>
// public class AsyncEnumerableStreamReader<T>(IAsyncEnumerable<T> requestStream, CancellationToken cancellationToken = default) : IAsyncStreamReader<T> {
//     readonly IAsyncEnumerator<T> _enumerator = requestStream.GetAsyncEnumerator(cancellationToken);
//
//     public T Current => _enumerator.Current;
//
//     public async Task<bool> MoveNext(CancellationToken cancellationToken) =>
//         await _enumerator.MoveNextAsync(cancellationToken);
// }
//
// public static class AsyncEnumerableStreamReaderExtensions {
//     public static IAsyncStreamReader<T> ToAsyncStreamReader<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default) =>
//         new AsyncEnumerableStreamReader<T>(source, cancellationToken);
// }
//
//
//
// delegate void Intercept<in TRequest>(TRequest request, ServerCallContext context) where TRequest : class;
//
// public abstract class ServerRequestInterceptor : Interceptor {
//     /// <summary>
//     /// Intercepts the incoming request message.
//     /// </summary>
//     protected abstract void Intercept<TRequest>(TRequest request, ServerCallContext context) where TRequest : class;
//
//     public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
//         TRequest request,
//         ServerCallContext context,
//         UnaryServerMethod<TRequest, TResponse> continuation
//     ) {
//         Intercept(request, context);
//         return continuation(request, context);
//     }
//
//     public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
//         IAsyncStreamReader<TRequest> requestStream,
//         ServerCallContext context,
//         ClientStreamingServerMethod<TRequest, TResponse> continuation
//     ) {
//         var interceptor = new StreamReaderInterceptor<TRequest, Intercept<TRequest>>(
//             requestStream, context, Intercept, static (req, intercept, ctx) => intercept(req, ctx));
//
//         return continuation(interceptor, context);
//     }
//
//     public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
//         TRequest request,
//         IServerStreamWriter<TResponse> responseStream,
//         ServerCallContext context,
//         ServerStreamingServerMethod<TRequest, TResponse> continuation
//     ) {
//         Intercept(request, context);
//         await continuation(request, responseStream, context);
//     }
//
//     public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
//         IAsyncStreamReader<TRequest> requestStream,
//         IServerStreamWriter<TResponse> responseStream,
//         ServerCallContext context,
//         DuplexStreamingServerMethod<TRequest, TResponse> continuation)
//     {
//         var interceptor = new StreamReaderInterceptor<TRequest, ServerRequestInterceptor>(
//             requestStream, context, this, static (req, interceptor, ctx) => interceptor.Intercept(req, ctx));
//
//         return continuation(interceptor, responseStream, context);
//     }
// }
//
// public delegate void FluentServerRequestIntercept<in TRequest, in TState>(TState state, TRequest request, ServerCallContext context);
// public sealed class FluentServerRequestInterceptor<TRequest, TState>(FluentServerRequestIntercept<TRequest, TState> intercept, TState state) : ServerRequestInterceptor where TRequest : class {
//     protected override void Intercept<T>(T request, ServerCallContext context) => intercept(state, (dynamic)request, context);
// }
//
// public delegate void FluentServerRequestIntercept<in TRequest>(TRequest request, ServerCallContext context);
// public sealed class FluentServerRequestInterceptor<TRequest>(FluentServerRequestIntercept<TRequest> intercept) : ServerRequestInterceptor where TRequest : class {
//     protected override void Intercept<T>(T request, ServerCallContext context) => intercept((dynamic)request, context);
// }
//
//
// // public delegate void InterceptRequest<in TRequest, in TState>(TRequest request, TState state, ServerCallContext context);
//
// //
// // public class Transformer<T>(TransformAsync<T> transform) {
// //     public IEnumerable<T> Transform(IEnumerable<T> source, CancellationToken cancellationToken) {
// //         foreach (var item in source) {
// //             // The 'await' expression can only be used in a method or lambda marked with the 'async' modifier
// //             // its not an async function but the transformer is, so we need to execute the ValueTask synchronously
// //             // in the most advanced performant allocation free possible
// //             // and this is not the way im sure
// //             yield return transform(item, cancellationToken).AsTask().GetAwaiter().GetResult();
// //         }
// //     }
// // }
//
//
//
//
//
// /// <summary>
// /// A delegate that defines a method signature for intercepting gRPC requests with additional state.
// /// </summary>
// public delegate ValueTask InterceptRequestAsync<in TRequest, in TState>(TRequest request, TState state, ServerCallContext context);
//
// class StreamReaderAsyncInterceptor<T, TState>(IAsyncStreamReader<T> requestStream, ServerCallContext context, TState state, InterceptRequestAsync<T, TState> intercept) : IAsyncStreamReader<T> {
//     public T Current { get; private set; } = default(T)!;
//
//     public async Task<bool> MoveNext(CancellationToken cancellationToken) {
//         var hasNext = await requestStream.MoveNext(cancellationToken);
//         if (hasNext)
//             await intercept(requestStream.Current, state, context);
//
//         Current = requestStream.Current;
//
//         return hasNext;
//     }
// }
//
// /// <summary>
// /// A delegate that defines a method signature for intercepting gRPC requests with additional state.
// /// </summary>
// public delegate void InterceptRequest<in TRequest, in TState>(TRequest request, TState state, ServerCallContext context);
//
// class StreamReaderInterceptor<T, TState>(IAsyncStreamReader<T> requestStream, ServerCallContext context, TState state, InterceptRequest<T, TState> intercept) : IAsyncStreamReader<T> {
//     public T Current { get; private set; } = default(T)!;
//
//     public async Task<bool> MoveNext(CancellationToken cancellationToken) {
//         var hasNext = await requestStream.MoveNext(cancellationToken);
//         if (hasNext)
//             intercept(requestStream.Current, state, context);
//
//         Current = requestStream.Current;
//
//         return hasNext;
//     }
// }
