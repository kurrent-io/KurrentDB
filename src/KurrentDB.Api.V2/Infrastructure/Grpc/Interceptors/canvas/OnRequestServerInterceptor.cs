// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using System.Threading.Channels;
// using Grpc.Core;
// using Grpc.Core.Interceptors;
//
// namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;
//
// // public abstract class OnRequestServerInterceptor : Interceptor {
// //     protected abstract ValueTask Intercept<TRequest>(TRequest request, ServerCallContext context) where TRequest : class;
// //
// //     public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
// //         TRequest request,
// //         ServerCallContext context,
// //         UnaryServerMethod<TRequest, TResponse> continuation
// //     ) {
// //         await Intercept(request, context);
// //         return await continuation(request, context);
// //     }
// //
// //     public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
// //         IAsyncStreamReader<TRequest> requestStream,
// //         ServerCallContext context,
// //         ClientStreamingServerMethod<TRequest, TResponse> continuation
// //     ) {
// //         var reader = new StreamReaderInterceptor<TRequest, ServerCallContext>(requestStream, context, Intercept);
// //         return continuation(reader, context);
// //     }
// //
// //     public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
// //         TRequest request,
// //         IServerStreamWriter<TResponse> responseStream,
// //         ServerCallContext context,
// //         ServerStreamingServerMethod<TRequest, TResponse> continuation
// //     ) {
// //         await Intercept(request, context);
// //         await continuation(request, responseStream, context);
// //     }
// //
// //     public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
// //         IAsyncStreamReader<TRequest> requestStream,
// //         IServerStreamWriter<TResponse> responseStream,
// //         ServerCallContext context,
// //         DuplexStreamingServerMethod<TRequest, TResponse> continuation) {
// //         var reader = new StreamReaderInterceptor<TRequest, ServerCallContext>(requestStream, context, Intercept);
// //         return continuation(reader, responseStream, context);
// //     }
// // }
//
//
// // /// <summary>
// // /// An interceptor for gRPC streaming requests that allows executing custom logic on each message received from the client.
// // /// It wraps an existing <see cref="IAsyncStreamReader{T}"/> and invokes a provided delegate for each message read from the stream.
// // /// This is useful for scenarios such as logging, validation, or authorization on a per-message basis in client-streaming or duplex-streaming gRPC calls.
// // /// </summary>
// // public class StreamReaderInterceptor<T, TState>(IAsyncStreamReader<T> requestStream,  TState state, InterceptRequest<T, TState> intercept) : IAsyncStreamReader<T> {
// //     public T Current { get; private set; } = default(T)!;
// //
// //     public async Task<bool> MoveNext(CancellationToken cancellationToken) {
// //         var hasNext = await requestStream.MoveNext(cancellationToken);
// //
// //         if (hasNext)
// //             await intercept(requestStream.Current, state);
// //
// //         Current = requestStream.Current;
// //
// //         return hasNext;
// //     }
// // }
// //
// // public class StreamReaderInterceptor<T>(IAsyncStreamReader<T> requestStream, InterceptRequest<T> intercept)
// //     : StreamReaderInterceptor<T, object>(requestStream, null!, async (req, _) => await intercept(req));
// //
// // public static class AsyncStreamReaderExtensions {
// //     public static IAsyncStreamReader<TRequest> OnEach<TRequest>(this IAsyncStreamReader<TRequest> requestStream, InterceptRequest<TRequest> intercept) =>
// //         new StreamReaderInterceptor<TRequest>(requestStream, intercept);
// //
// //     public static IAsyncStreamReader<TRequest> OnEach<TRequest>(this IAsyncStreamReader<TRequest> requestStream, InterceptRequest<TRequest, ServerCallContext> intercept, ServerCallContext context) =>
// //         new StreamReaderInterceptor<TRequest, ServerCallContext>(requestStream, context, intercept);
// //
// //     public static IAsyncStreamReader<TRequest> OnEach<TRequest, TState>(this IAsyncStreamReader<TRequest> requestStream, InterceptRequest<TRequest, TState> intercept, TState state) =>
// //         new StreamReaderInterceptor<TRequest, TState>(requestStream, state, intercept);
// // }
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
//         await _enumerator.MoveNextAsync(cancellationToken); // this one or only the main one or both?
// }
//
// // public class ChannelStreamReader<T>(ChannelReader<T> reader, CancellationToken cancellationToken)
// //     : AsyncEnumerableStreamReader<T>(reader.ReadAllAsync(cancellationToken), cancellationToken);
// //
// // // IClientStreamWriter<T>
// //
// // public class ChannelStreamWriter<T>(ChannelWriter<T> writer) : IServerStreamWriter<T> {
// //     public WriteOptions? WriteOptions { get; set; }
// //
// //     public async Task WriteAsync(T message) => await writer.WriteAsync(message);
// // }
//
// public static class AsyncEnumerableStreamReaderExtensions {
//     public static IAsyncStreamReader<T> ToAsyncStreamReader<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default) =>
//         new AsyncEnumerableStreamReader<T>(source, cancellationToken);
//
//     // public static IAsyncStreamReader<T> ToChannelStreamReader<T>(this ChannelReader<T> source, CancellationToken cancellationToken = default) =>
//     //     new ChannelStreamReader<T>(source, cancellationToken);
// }
//
// /// <summary>
// /// A server interceptor that executes custom logic on each incoming request message.
// /// It supports unary, client-streaming, server-streaming, and duplex-streaming RPCs.
// /// This is useful for scenarios such as logging, validation, or authorization on a per-message basis.
// /// </summary>
// public abstract class OnRequestServerInterceptor : Interceptor {
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
//
// /// <summary>
// /// A modern server interceptor that uses IAsyncEnumerable for all streaming operations,
// /// providing a cleaner and more powerful API for interceptor implementations.
// /// </summary>
// public abstract class ModernInterceptor : Interceptor {
//     /// <summary>
//     /// Intercept unary calls (single request/response).
//     /// </summary>
//     protected abstract ValueTask<TResponse> InterceptUnaryAsync<TRequest, TResponse>(
//         TRequest request,
//         ServerCallContext context,
//         Func<TRequest, Task<TResponse>> next)
//         where TRequest : class
//         where TResponse : class;
//
//     /// <summary>
//     /// Intercept client streaming (multiple requests, single response).
//     /// </summary>
//     protected abstract ValueTask<TResponse> InterceptClientStreamAsync<TRequest, TResponse>(
//         IAsyncEnumerable<TRequest> requests,
//         ServerCallContext context,
//         Func<IAsyncEnumerable<TRequest>, Task<TResponse>> next)
//         where TRequest : class
//         where TResponse : class;
//
//     /// <summary>
//     /// Intercept server streaming (single request, multiple responses).
//     /// </summary>
//     protected abstract IAsyncEnumerable<TResponse> InterceptServerStreamAsync<TRequest, TResponse>(
//         TRequest request,
//         ServerCallContext context,
//         Func<TRequest, IAsyncEnumerable<TResponse>> next)
//         where TRequest : class
//         where TResponse : class;
//
//     /// <summary>
//     /// Intercept duplex streaming (multiple requests, multiple responses).
//     /// </summary>
//     protected abstract IAsyncEnumerable<TResponse> InterceptDuplexStreamAsync<TRequest, TResponse>(
//         IAsyncEnumerable<TRequest> requests,
//         ServerCallContext context,
//         Func<IAsyncEnumerable<TRequest>, IAsyncEnumerable<TResponse>> next)
//         where TRequest : class
//         where TResponse : class;
//
//     // ===== Implementation that bridges old and new APIs =====
//
//     public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
//         TRequest request,
//         ServerCallContext context,
//         UnaryServerMethod<TRequest, TResponse> continuation
//     ) {
//         return await InterceptUnaryAsync(
//             request, context,
//             async req => await continuation(req, context)
//         );
//     }
//
//     public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
//         IAsyncStreamReader<TRequest> requestStream,
//         ServerCallContext context,
//         ClientStreamingServerMethod<TRequest, TResponse> continuation
//     ) {
//         return await InterceptClientStreamAsync(
//             requestStream.ReadAllAsync(context.CancellationToken), context,
//             async stream => await continuation(stream.ToAsyncStreamReader(), context)
//         );
//     }
//
//     public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
//         TRequest request,
//         IServerStreamWriter<TResponse> responseStream,
//         ServerCallContext context,
//         ServerStreamingServerMethod<TRequest, TResponse> continuation
//     ) {
//         var responses = InterceptServerStreamAsync<TRequest, TResponse>(
//             request, context,
//             req => ProduceResponses(req, context, continuation)
//         );
//
//         // Write the async enumerable to the stream
//         await foreach (var response in responses.WithCancellation(context.CancellationToken))
//             await responseStream.WriteAsync(response);
//
//         return;
//
//         static async IAsyncEnumerable<TResponse> ProduceResponses(
//             TRequest request,
//             ServerCallContext context,
//             ServerStreamingServerMethod<TRequest, TResponse> continuation
//         )  {
//             var buffer = Channel.CreateUnbounded<TResponse>();
//
//             var continuationTask = Task.Run(async () => {
//                 var writer = new ChannelStreamWriter<TResponse>(buffer.Writer);
//                 try {
//                     await continuation(request, writer, context);
//                 }
//                 finally {
//                     buffer.Writer.Complete();
//                 }
//             });
//
//             // Yield the responses as they come
//             await foreach (var response in buffer.Reader.ReadAllAsync(context.CancellationToken))
//                 yield return response;
//
//             await continuationTask; // Ensure any exceptions are observed
//         }
//     }
//
//     public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
//         IAsyncStreamReader<TRequest> requestStream,
//         IServerStreamWriter<TResponse> responseStream,
//         ServerCallContext context,
//         DuplexStreamingServerMethod<TRequest, TResponse> continuation
//     ) {
//         var responses = InterceptDuplexStreamAsync(
//             requestStream.ReadAllAsync(context.CancellationToken), context,
//             requests => ProduceResponses(requests, context, continuation)
//         );
//
//         await foreach (var response in responses.WithCancellation(context.CancellationToken))
//             await responseStream.WriteAsync(response);
//
//         return;
//
//         static async IAsyncEnumerable<TResponse> ProduceResponses(
//             IAsyncEnumerable<TRequest> requests,
//             ServerCallContext context,
//             DuplexStreamingServerMethod<TRequest, TResponse> continuation
//         ) {
//             var buffer = Channel.CreateUnbounded<TResponse>();
//
//             var continuationTask = Task.Run(async () => {
//                 var reader = requests.ToAsyncStreamReader(context.CancellationToken);
//                 var writer = new ChannelStreamWriter<TResponse>(buffer.Writer);
//
//                 try {
//                     await continuation(reader, writer, context);
//                 }
//                 finally {
//                     buffer.Writer.Complete();
//                 }
//             });
//
//             await foreach (var response in buffer.Reader.ReadAllAsync(context.CancellationToken)) {
//                 yield return response;
//             }
//
//             await continuationTask;
//         }
//     }
//
//     class ChannelStreamWriter<T>(ChannelWriter<T> writer) : IServerStreamWriter<T> {
//         public WriteOptions? WriteOptions { get; set; }
//
//         public async Task WriteAsync(T message) => await writer.WriteAsync(message);
//     }
// }
