// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using KurrentDB.Core.Services.Transport.Grpc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

public class GrpcStreamingShutdownMiddlewareTests {
	// A streaming call is open-ended, so it must observe shutdown: while the call is in flight the middleware
	// should have linked ApplicationStopping into the token the handler reads (HttpContext.RequestAborted).
	[Theory]
	[InlineData(MethodType.ClientStreaming)]
	[InlineData(MethodType.ServerStreaming)]
	[InlineData(MethodType.DuplexStreaming)]
	public async Task cancels_streaming_call_when_application_is_stopping(MethodType methodType) {
		using var lifetime = new FakeLifetime();
		var context = ContextForMethod(methodType);
		var tokenObservedCancelled = false;

		var sut = new GrpcStreamingShutdownMiddleware(ctx => {
			lifetime.StopApplication();
			tokenObservedCancelled = ctx.RequestAborted.IsCancellationRequested;
			return Task.CompletedTask;
		}, lifetime);

		await sut.Invoke(context);

		Assert.True(tokenObservedCancelled);
	}

	// A unary call is finite and should keep draining gracefully: the middleware must NOT link shutdown into it,
	// so beginning shutdown while it runs does not cancel the call.
	[Fact]
	public async Task does_not_cancel_unary_call_when_application_is_stopping() {
		using var lifetime = new FakeLifetime();
		var context = ContextForMethod(MethodType.Unary);
		var tokenObservedCancelled = true;

		var sut = new GrpcStreamingShutdownMiddleware(ctx => {
			lifetime.StopApplication();
			tokenObservedCancelled = ctx.RequestAborted.IsCancellationRequested;
			return Task.CompletedTask;
		}, lifetime);

		await sut.Invoke(context);

		Assert.False(tokenObservedCancelled);
	}

	// The linked token must still fire on the original trigger (client disconnect), not only on shutdown.
	[Fact]
	public async Task streaming_call_still_observes_client_disconnect() {
		using var lifetime = new FakeLifetime();
		using var clientDisconnect = new CancellationTokenSource();
		var context = ContextForMethod(MethodType.ServerStreaming);
		context.RequestAborted = clientDisconnect.Token;
		var tokenObservedCancelled = false;

		var sut = new GrpcStreamingShutdownMiddleware(ctx => {
			clientDisconnect.Cancel();
			tokenObservedCancelled = ctx.RequestAborted.IsCancellationRequested;
			return Task.CompletedTask;
		}, lifetime);

		await sut.Invoke(context);

		Assert.True(tokenObservedCancelled);
	}

	// Non-gRPC requests (no GrpcMethodMetadata) pass straight through and are unaffected by shutdown.
	[Fact]
	public async Task ignores_non_grpc_request() {
		using var lifetime = new FakeLifetime();
		var context = new DefaultHttpContext(); // no endpoint / no gRPC metadata
		var nextCalled = false;
		var tokenObservedCancelled = true;

		var sut = new GrpcStreamingShutdownMiddleware(ctx => {
			nextCalled = true;
			lifetime.StopApplication();
			tokenObservedCancelled = ctx.RequestAborted.IsCancellationRequested;
			return Task.CompletedTask;
		}, lifetime);

		await sut.Invoke(context);

		Assert.True(nextCalled);
		Assert.False(tokenObservedCancelled);
	}

	static DefaultHttpContext ContextForMethod(MethodType methodType) {
		var context = new DefaultHttpContext();
		context.SetEndpoint(new Endpoint(
			_ => Task.CompletedTask,
			new EndpointMetadataCollection(new GrpcMethodMetadata(typeof(object), new FakeMethod(methodType))),
			displayName: "test"));
		return context;
	}

	sealed class FakeMethod(MethodType type) : IMethod {
		public MethodType Type => type;
		public string ServiceName => "svc";
		public string Name => "method";
		public string FullName => "/svc/method";
	}

	sealed class FakeLifetime : IHostApplicationLifetime, IDisposable {
		readonly CancellationTokenSource _stopping = new();
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => _stopping.Token;
		public CancellationToken ApplicationStopped => CancellationToken.None;
		public void StopApplication() => _stopping.Cancel();
		public void Dispose() => _stopping.Dispose();
	}
}
