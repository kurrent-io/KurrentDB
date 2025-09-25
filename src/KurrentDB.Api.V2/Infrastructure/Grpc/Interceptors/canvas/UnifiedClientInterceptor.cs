// using Grpc.Core;
// using Grpc.Core.Interceptors;
//
// namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;
//
// /// <summary>
// /// A generic interceptor for the client side that allows intercepting all types of gRPC calls (unary, client streaming, server streaming, and duplex streaming)
// /// by implementing a single abstract method <see cref="Intercept{TRequest}(TRequest, ClientInterceptorContext{TRequest, TResponse})"/>.
// /// This method is called for each outgoing request, allowing for centralized handling of cross-cutting concerns such as logging, authentication, or validation.
// /// </summary>
// public abstract class UnifiedClientInterceptor : Interceptor {
//     protected abstract ValueTask Intercept<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context)
//         where TRequest : class
//         where TResponse : class;
//
//     public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
//         TRequest request,
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncUnaryCallContinuation<TRequest, TResponse> continuation
//     ) {
//         // // Run intercept synchronously (blocking) or fire-and-forget
//         // var interceptTask = Intercept(request, context);
//         // // For unary calls, we can await the intercept before making the call
//         // if (!interceptTask.IsCompletedSuccessfully)
//         //     interceptTask.AsTask().GetAwaiter().GetResult();
//         // return continuation(request, context);
//
//         var call = continuation(request, context);
//
//         return new AsyncUnaryCall<TResponse>(Hijack(call.ResponseAsync), call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);
//
//         async Task<TResponse> Hijack(Task<TResponse> responseTask) {
//             await Intercept(request, context);
//             return await responseTask;
//         }
//     }
//
//     public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation
//     ) {
//         var call = continuation(context);
//
//         // Wrap the request stream to intercept each message being sent
//         var wrappedRequestStream = new StreamWriterInterceptor<TRequest, ClientInterceptorContext<TRequest, TResponse>>(
//             call.RequestStream, context,
//             async (req, ctx) => await Intercept(req, ctx)
//         );
//
//         return new AsyncClientStreamingCall<TRequest, TResponse>(
//             wrappedRequestStream,
//             call.ResponseAsync,
//             call.ResponseHeadersAsync,
//             call.GetStatus,
//             call.GetTrailers,
//             call.Dispose
//         );
//     }
//
//     public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
//         TRequest request,
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
//     ) {
//         // Intercept the initial request
//         var interceptTask = Intercept(request, context);
//
//         if (!interceptTask.IsCompletedSuccessfully) interceptTask.AsTask().GetAwaiter().GetResult();
//
//         return continuation(request, context);
//     }
//
//     public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
//         ClientInterceptorContext<TRequest, TResponse> context,
//         AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation
//     ) {
//         var call = continuation(context);
//
//         // Wrap the request stream to intercept each message being sent
//         var wrappedRequestStream = new StreamWriterInterceptor<TRequest, ClientInterceptorContext<TRequest, TResponse>>(
//             call.RequestStream,
//             context,
//             async (req, ctx) => await Intercept(req, ctx)
//         );
//
//         return new AsyncDuplexStreamingCall<TRequest, TResponse>(
//             wrappedRequestStream,
//             call.ResponseStream,
//             call.ResponseHeadersAsync,
//             call.GetStatus,
//             call.GetTrailers,
//             call.Dispose
//         );
//     }
// }
//
// /// <summary>
// /// An interceptor for gRPC streaming requests that allows executing custom logic on each message sent to the server.
// /// It wraps an existing <see cref="IClientStreamWriter{T}"/> and invokes a provided delegate for each message written to the stream.
// /// This is useful for scenarios such as logging, validation, or adding metadata on a per-message basis in client-streaming or duplex-streaming gRPC calls.
// /// </summary>
// public class StreamWriterInterceptor<T, TState>(IClientStreamWriter<T> requestStream, TState state, InterceptRequest<T, TState> intercept )
//     : IClientStreamWriter<T> where T : class {
//     public WriteOptions? WriteOptions {
//         get => requestStream.WriteOptions;
//         set => requestStream.WriteOptions = value;
//     }
//
//     public async Task WriteAsync(T message) {
//         await intercept(message, state);
//         await requestStream.WriteAsync(message);
//     }
//
//     public Task CompleteAsync() => requestStream.CompleteAsync();
// }
//
// public class StreamWriterInterceptor<T>(IClientStreamWriter<T> requestStream, InterceptRequest<T> intercept)
//     : StreamWriterInterceptor<T, object>(requestStream, null!, async (req, _) => await intercept(req)) where T : class;
//
// public static class ClientStreamWriterExtensions {
//     public static IClientStreamWriter<TRequest> OnEach<TRequest>(
//         this IClientStreamWriter<TRequest> writer,
//         InterceptRequest<TRequest> intercept
//     ) where TRequest : class =>
//         new StreamWriterInterceptor<TRequest>(writer, intercept);
//
//     public static IClientStreamWriter<TRequest> OnEach<TRequest, TState>(
//         this IClientStreamWriter<TRequest> writer,
//         InterceptRequest<TRequest, TState> intercept,
//         TState state
//     ) where TRequest : class =>
//         new StreamWriterInterceptor<TRequest, TState>(writer, state, intercept);
// }
