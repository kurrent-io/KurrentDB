// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Serilog;

namespace EventStore.Core.Services.Transport.Grpc;

// Detect calls that have been automatically retried per https://github.com/grpc/proposal/blob/master/A6-client-retries.md
// such calls may not have been intended by the user and as such may disguise a network interruption as a slow server response
class RetryInterceptor : Interceptor {
	static readonly ILogger Log = Serilog.Log.ForContext<RetryInterceptor>();

	public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		ServerCallContext context,
		ClientStreamingServerMethod<TRequest, TResponse> continuation) {

		LogRetries(context);
		return base.ClientStreamingServerHandler(requestStream, context, continuation);
	}

	public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		DuplexStreamingServerMethod<TRequest, TResponse> continuation) {

		LogRetries(context);
		return base.DuplexStreamingServerHandler(requestStream, responseStream, context, continuation);
	}

	public override Task ServerStreamingServerHandler<TRequest, TResponse>(
		TRequest request,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		ServerStreamingServerMethod<TRequest, TResponse> continuation) {

		LogRetries(context);
		return base.ServerStreamingServerHandler(request, responseStream, context, continuation);
	}

	public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request,
		ServerCallContext context,
		UnaryServerMethod<TRequest, TResponse> continuation) {

		LogRetries(context);
		return base.UnaryServerHandler(request, context, continuation);
	}

	private static void LogRetries(ServerCallContext context) {
		var entry = context.RequestHeaders.Get("grpc-previous-rpc-attempts");
		if (entry is null)
			return;

		Log.Information("gRPC call to {method} received after {retries} retries", context.Method, entry.Value);
	}
}
