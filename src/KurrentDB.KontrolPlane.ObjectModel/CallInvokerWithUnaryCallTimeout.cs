// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;

namespace KurrentDB;

public static class CallInvokerExtensions {
	public static CallInvoker WithUnaryTimeout(this CallInvoker self, int timeoutMs) =>
		WithUnaryTimeout(self, TimeSpan.FromMilliseconds(timeoutMs));

	public static CallInvoker WithUnaryTimeout(this CallInvoker self, TimeSpan timeout) =>
		new CallInvokerWithUnaryCallTimeout(self, timeout);
}

file sealed class CallInvokerWithUnaryCallTimeout(CallInvoker invoker, TimeSpan timeout) : CallInvoker {
	public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
		string? host,
		CallOptions options,
		TRequest request)
		=> invoker.BlockingUnaryCall(method,
			host,
			options.Deadline is null ? options.WithDeadline(DateTime.UtcNow + timeout) : options,
			request);

	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
		string? host,
		CallOptions options,
		TRequest request)
		=> invoker.AsyncUnaryCall(method,
			host,
			options.Deadline is null ? options.WithDeadline(DateTime.UtcNow + timeout) : options,
			request);

	public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
		string? host,
		CallOptions options,
		TRequest request)
		=> invoker.AsyncServerStreamingCall(method, host, options, request);

	public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method,
		string? host,
		CallOptions options)
		=> invoker.AsyncClientStreamingCall(method, host, options);

	public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method,
		string? host,
		CallOptions options)
		=> invoker.AsyncDuplexStreamingCall(method, host, options);
}
