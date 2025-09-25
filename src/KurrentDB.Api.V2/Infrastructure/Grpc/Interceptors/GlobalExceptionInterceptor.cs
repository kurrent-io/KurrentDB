// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using Grpc.Core.Interceptors;
using KurrentDB.Api.Errors;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Infrastructure.Grpc.Interceptors;

public class GlobalExceptionInterceptor(ILogger<GlobalExceptionInterceptor> logger) : Interceptor {
	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request,
		ServerCallContext context,
		UnaryServerMethod<TRequest, TResponse> continuation) {
		try {
			return await continuation(request, context);
		} catch (Exception ex) {
			throw TransformException(ex, context);
		}
	}

	public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		ServerCallContext context,
		ClientStreamingServerMethod<TRequest, TResponse> continuation) {
		try {
			return await continuation(requestStream, context);
		} catch (Exception ex) {
			throw TransformException(ex, context);
		}
	}

	public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
		TRequest request,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		ServerStreamingServerMethod<TRequest, TResponse> continuation) {
		try {
			await continuation(request, responseStream, context);
		} catch (Exception ex) {
			throw TransformException(ex, context);
		}
	}

	public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
		IAsyncStreamReader<TRequest> requestStream,
		IServerStreamWriter<TResponse> responseStream,
		ServerCallContext context,
		DuplexStreamingServerMethod<TRequest, TResponse> continuation) {
		try {
			await continuation(requestStream, responseStream, context);
		} catch (Exception ex) {
			throw TransformException(ex, context);
		}
	}

	RpcException TransformException(Exception exception, ServerCallContext context) {
		// Log the original exception
		logger.LogError(exception, "An error occurred in gRPC call {Method}", context.Method);

		if (exception is RpcException rpcException)
			return rpcException; // Bypass if it's already an RpcException

		// Need to handle this more causiosly in the future
		// because I need to understand what rpc exceptions are being thrown by grpc itself
		// and not mask them all as internal server errors
		// Actually I also need to understand WHEN the grpc exceptions are thrown by grpc itself
		// because I think they are thrown when the client disconnects mid-call
		// and in that case we should not log them as errors

		return exception switch {
			RpcException rex => rex,  // Bypass if it's already an RpcException
			_                => ApiErrors.InternalServerError(exception)
		};
	}
}
