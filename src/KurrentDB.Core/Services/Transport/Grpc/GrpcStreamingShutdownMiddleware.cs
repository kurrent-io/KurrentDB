// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Core.Services.Transport.Grpc;

// Cancels open streaming gRPC calls when the server begins shutting down.
//
// On a graceful shutdown Kestrel waits for calls to end of their own accord before forcing the shutdown, but
// streaming gRPC calls (e.g. subscriptions) can be long lived, resulting in a timeout and forced shutdown.
//
// This middleware links IHostApplicationLifetime.ApplicationStopping (which trips the instant shutdown begins)
// into the call by swapping HttpContext.RequestAborted for a linked token. gRPC's
// ServerCallContext.CancellationToken is backed by RequestAborted, so the handler's existing read loop ends
// immediately and Kestrel drains in milliseconds. No per-handler or per-service changes are needed.
//
// Applies to every streaming method (client-, server-, and duplex-streaming) — any of them can stay open under
// client control and stall the drain; only unary calls are inherently finite and keep draining gracefully.
// It swaps RequestAborted BEFORE the gRPC ServerCallContext is constructed, so the swap is guaranteed to
// propagate even when the call carries a deadline (this would not be the case if implemented as an interceptor).
public sealed class GrpcStreamingShutdownMiddleware(RequestDelegate next, IHostApplicationLifetime lifetime) {
	public Task Invoke(HttpContext context) {
		var methodType = context.GetEndpoint()?.Metadata.GetMetadata<GrpcMethodMetadata>()?.Method.Type;
		return methodType
			is MethodType.ClientStreaming
			or MethodType.ServerStreaming
			or MethodType.DuplexStreaming
			? InvokeStreaming(context)
			: next(context);
	}

	async Task InvokeStreaming(HttpContext context) {
		var requestAborted = context.RequestAborted;
		var linked = CancellationToken.Combine(
			requestAborted,
			lifetime.ApplicationStopping);
		context.RequestAborted = linked.Token;

		try {
			await next(context);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == linked.Token) {
			throw new OperationCanceledException(ex.Message, ex, linked.CancellationOrigin);
		} finally {
			context.RequestAborted = requestAborted;
			linked.Dispose();
		}
	}
}
