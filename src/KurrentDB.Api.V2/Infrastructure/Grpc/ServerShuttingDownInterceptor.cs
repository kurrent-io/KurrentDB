// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Grpc.Core.Interceptors;
using KurrentDB.Api.Errors;
using KurrentDB.Core;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Api.Infrastructure.Grpc;

// Gives clients an actionable status when a streaming call ends because the server is shutting down.
//
// GrpcStreamingShutdownMiddleware cancels open streaming calls on ApplicationStopping so Kestrel drains promptly.
// This interceptor translates the shutdown-driven cancellations into ApiErrors.ServerShuttingDown() (UNAVAILABLE
// + rich ErrorInfo reason ServerShuttingDown). Rich-status-aware clients read the reason and can
// distinguish a planned shutdown from an unknown disconnection (which is just UNAVAILABLE).
// Without this interceptor the OperationCanceledException is translated to status UNKNOWN.
// Unary calls are not dealt with because they are not canceled on shutdown but left to complete.
public sealed class ServerShuttingDownInterceptor(
	IHostApplicationLifetime lifetime,
	ClusterVNodeOptions options)
	: Interceptor {

	// A single node has to restart before it is available again, so suggest a longer backoff; in a multi-node
	// cluster the client can fail over to another node instead, so a short backoff is enough.
	readonly TimeSpan _retryAfter = options.Cluster.ClusterSize <= 1
		? TimeSpan.FromSeconds(5)
		: TimeSpan.FromSeconds(1);

	// Best effort. Here we do not know if the context was cancelled because of the original http context RequestAborted
	// or because of the lifetime ApplicationStopping which we linked, or the deadline cancellation which can also be linked.
	// It's not that bad to attribute any of these to shutting down if we are, in fact, shutting down.
	bool IsCausedByShutdown(ServerCallContext context, OperationCanceledException ex) =>
		ex.CancellationToken == context.CancellationToken &&
		lifetime.ApplicationStopping.IsCancellationRequested;

	public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		ServerCallContext context,
		ClientStreamingServerMethod<TRequest, TResponse> continuation) {

		try {
			return await continuation(requestStream, context);
		} catch (OperationCanceledException ex) when (IsCausedByShutdown(context, ex)) {
			throw ApiErrors.ServerShuttingDown(_retryAfter);
		}
	}

	public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
		TRequest request,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		ServerStreamingServerMethod<TRequest, TResponse> continuation) {

		try {
			await continuation(request, responseStream, context);
		} catch (OperationCanceledException ex) when (IsCausedByShutdown(context, ex)) {
			throw ApiErrors.ServerShuttingDown(_retryAfter);
		}
	}

	public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		DuplexStreamingServerMethod<TRequest, TResponse> continuation) {

		try {
			await continuation(requestStream, responseStream, context);
		} catch (OperationCanceledException ex) when (IsCausedByShutdown(context, ex)) {
			throw ApiErrors.ServerShuttingDown(_retryAfter);
		}
	}
}
