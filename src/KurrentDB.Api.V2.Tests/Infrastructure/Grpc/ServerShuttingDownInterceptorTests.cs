// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure.Grpc;
using KurrentDB.Core;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Api.Tests.Infrastructure.Grpc;

[Category("Grpc")]
public class ServerShuttingDownInterceptorTests {
	// ApiErrors.ServerShuttingDown() is the payload the interceptor throws — verify it is the canonical
	// retryable status with the ServerShuttingDown reason (distinct from ServerNotReady) and a retry hint.
	[Test]
	public async Task server_shutting_down_is_unavailable_with_shutdown_reason() {
		var rex = ApiErrors.ServerShuttingDown();

		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.Unavailable);

		var errorInfo = rex.GetRpcStatus()?.GetDetail<ErrorInfo>();
		await Assert.That(errorInfo).IsNotNull();
		await Assert.That(errorInfo!.Reason).IsEqualTo("SERVER_SHUTTING_DOWN");
	}

	[Test]
	public async Task server_shutting_down_carries_retry_info() {
		var rex = ApiErrors.ServerShuttingDown(TimeSpan.FromSeconds(30));

		var retryInfo = rex.GetRpcStatus()?.GetDetail<RetryInfo>();
		await Assert.That(retryInfo).IsNotNull();
		await Assert.That(retryInfo!.RetryDelay.ToTimeSpan()).IsEqualTo(TimeSpan.FromSeconds(30));
	}

	// When the call was cancelled AND the server is shutting down, the interceptor translates the resulting
	// OperationCanceledException into the ServerShuttingDown status. (The three streaming overrides share this
	// logic; ServerStreaming is exercised here as the representative case.)
	[Test]
	public async Task translates_shutdown_cancellation_to_server_shutting_down() {
		using var stoppingCts = new CancellationTokenSource();
		using var callCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingCts.Token);
		await stoppingCts.CancelAsync();

		var rex = await Assert.That(async () => await CreateSut(stoppingCts.Token)
				.ServerStreamingServerHandler<string, string>(
					"request", new FakeServerStreamWriter<string>(), new FakeServerCallContext(callCts.Token),
					(_, _, context) => Task.FromCanceled(context.CancellationToken)))
			.Throws<RpcException>();

		await Assert.That(rex!.StatusCode).IsEqualTo(StatusCode.Unavailable);
		await Assert.That(rex.GetRpcStatus()?.GetDetail<ErrorInfo>()?.Reason).IsEqualTo("SERVER_SHUTTING_DOWN");
	}

	// The suggested retry delay depends on cluster size: a single node must restart (longer wait), whereas a
	// cluster client can fail over to another node (shorter wait).
	[Test]
	[Arguments(1, 5)]
	[Arguments(3, 1)]
	public async Task retry_after_reflects_cluster_size(int clusterSize, int expectedSeconds) {
		using var stoppingCts = new CancellationTokenSource();
		using var callCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingCts.Token);
		await stoppingCts.CancelAsync();

		var rex = await Assert.That(async () => await CreateSut(stoppingCts.Token, clusterSize)
				.ServerStreamingServerHandler<string, string>(
					"request", new FakeServerStreamWriter<string>(), new FakeServerCallContext(callCts.Token),
					(_, _, context) => Task.FromCanceled(context.CancellationToken)))
			.Throws<RpcException>();

		var retryInfo = rex!.GetRpcStatus()?.GetDetail<RetryInfo>();
		await Assert.That(retryInfo).IsNotNull();
		await Assert.That(retryInfo!.RetryDelay.ToTimeSpan()).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
	}

	// A client-initiated cancel (server NOT shutting down) is left to propagate unchanged.
	[Test]
	public async Task propagates_client_cancellation_when_not_shutting_down() {
		using var stoppingCts = new CancellationTokenSource();
		using var callCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingCts.Token);
		await callCts.CancelAsync();

		await Assert.That(async () => await CreateSut(stoppingCts.Token)
				.ServerStreamingServerHandler<string, string>(
					"request", new FakeServerStreamWriter<string>(), new FakeServerCallContext(callCts.Token),
					(_, _, context) => Task.FromCanceled(context.CancellationToken)))
			.Throws<OperationCanceledException>();
	}

	// A call that completes normally passes straight through.
	[Test]
	public async Task passes_through_a_successful_call() {
		var continuationRan = false;

		await CreateSut(CancellationToken.None).ServerStreamingServerHandler<string, string>(
			"request", new FakeServerStreamWriter<string>(), new FakeServerCallContext(CancellationToken.None),
			(_, _, _) => {
				continuationRan = true;
				return Task.CompletedTask;
			});

		await Assert.That(continuationRan).IsTrue();
	}

	static ServerShuttingDownInterceptor CreateSut(CancellationToken stopping, int clusterSize = 1) =>
		new(new FakeLifetime(stopping), new ClusterVNodeOptions { Cluster = new() { ClusterSize = clusterSize } });

	sealed class FakeLifetime(CancellationToken stopping) : IHostApplicationLifetime {
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => stopping;
		public CancellationToken ApplicationStopped => CancellationToken.None;
		public void StopApplication() { }
	}

	sealed class FakeServerStreamWriter<T> : IServerStreamWriter<T> {
		public WriteOptions? WriteOptions { get; set; }
		public Task WriteAsync(T message) => Task.CompletedTask;
	}

	sealed class FakeServerCallContext(CancellationToken cancellationToken) : ServerCallContext {
		protected override CancellationToken CancellationTokenCore => cancellationToken;
		protected override Metadata RequestHeadersCore { get; } = new();
		protected override Metadata ResponseTrailersCore { get; } = new();
		protected override global::Grpc.Core.Status StatusCore { get; set; }
		protected override WriteOptions? WriteOptionsCore { get; set; }
		protected override AuthContext AuthContextCore { get; } = new(string.Empty, new Dictionary<string, List<AuthProperty>>());
		protected override string MethodCore => "method";
		protected override string HostCore => "host";
		protected override string PeerCore => "peer";
		protected override DateTime DeadlineCore => DateTime.MaxValue;
		protected override IDictionary<object, object> UserStateCore { get; } = new Dictionary<object, object>();
		protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
		protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
	}
}
