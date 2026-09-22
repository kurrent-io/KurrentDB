// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace KurrentDB.Core.Services.Transport.Grpc;

/// <summary>
/// Refuses calls until the node is ready to serve them, with the status peers retry on.
/// </summary>
/// <remarks>
/// An interceptor rather than an endpoint filter: a filter runs outside the gRPC pipeline, so an
/// <see cref="RpcException"/> thrown there is never translated into a gRPC status and reaches the
/// caller as HTTP 500, which is indistinguishable from the node being broken.
/// </remarks>
class NotReadyInterceptor(Func<bool> isReady) : Interceptor {
	public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request,
		ServerCallContext context,
		UnaryServerMethod<TRequest, TResponse> continuation) {

		ThrowIfNotReady();
		return base.UnaryServerHandler(request, context, continuation);
	}

	public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		ServerCallContext context,
		ClientStreamingServerMethod<TRequest, TResponse> continuation) {

		ThrowIfNotReady();
		return base.ClientStreamingServerHandler(requestStream, context, continuation);
	}

	public override Task ServerStreamingServerHandler<TRequest, TResponse>(
		TRequest request,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		ServerStreamingServerMethod<TRequest, TResponse> continuation) {

		ThrowIfNotReady();
		return base.ServerStreamingServerHandler(request, responseStream, context, continuation);
	}

	public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		DuplexStreamingServerMethod<TRequest, TResponse> continuation) {

		ThrowIfNotReady();
		return base.DuplexStreamingServerHandler(requestStream, responseStream, context, continuation);
	}

	private void ThrowIfNotReady() {
		if (!isReady())
			throw new RpcException(new Status(StatusCode.Unavailable, "The node is not ready yet."));
	}
}
