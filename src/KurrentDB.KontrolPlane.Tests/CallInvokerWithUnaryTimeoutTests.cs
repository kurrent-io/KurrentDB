// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Grpc.Core;

namespace KurrentDB.KontrolPlane;

public class CallInvokerWithUnaryTimeoutTests {
	[Fact]
	public void a_unary_call_is_given_the_timeout_as_its_deadline() {
		var inner = new RecordingCallInvoker();
		var invoker = inner.WithUnaryTimeout(TimeSpan.FromSeconds(30));

		var before = DateTime.UtcNow;
		invoker.AsyncUnaryCall(UnaryMethod, host: null, new CallOptions(), request: "");
		var after = DateTime.UtcNow;

		var deadline = Assert.NotNull(inner.LastOptions.Deadline);
		Assert.InRange(deadline, before.AddSeconds(30), after.AddSeconds(30));
	}

	[Fact]
	public void a_deadline_the_caller_asked_for_is_left_alone() {
		var inner = new RecordingCallInvoker();
		var invoker = inner.WithUnaryTimeout(TimeSpan.FromSeconds(30));
		var callersDeadline = DateTime.UtcNow.AddHours(1);

		invoker.AsyncUnaryCall(UnaryMethod, host: null, new CallOptions(deadline: callersDeadline), request: "");

		Assert.Equal(callersDeadline, inner.LastOptions.Deadline);
	}

	// The announce stream is open for as long as the node is up, so a deadline on it would tear the
	// stream down on every timeout and the node would stop hearing about its own database.
	[Fact]
	public void a_streaming_call_is_not_given_a_deadline() {
		var inner = new RecordingCallInvoker();
		var invoker = inner.WithUnaryTimeout(TimeSpan.FromSeconds(30));

		invoker.AsyncServerStreamingCall(StreamingMethod, host: null, new CallOptions(), request: "");
		Assert.Null(inner.LastOptions.Deadline);

		invoker.AsyncDuplexStreamingCall(DuplexMethod, host: null, new CallOptions());
		Assert.Null(inner.LastOptions.Deadline);

		invoker.AsyncClientStreamingCall(ClientStreamingMethod, host: null, new CallOptions());
		Assert.Null(inner.LastOptions.Deadline);
	}

	private static readonly Marshaller<string> Marshaller = Marshallers.Create(
		Encoding.UTF8.GetBytes, Encoding.UTF8.GetString);

	private static readonly Method<string, string> UnaryMethod =
		new(MethodType.Unary, "svc", "unary", Marshaller, Marshaller);

	private static readonly Method<string, string> StreamingMethod =
		new(MethodType.ServerStreaming, "svc", "serverStreaming", Marshaller, Marshaller);

	private static readonly Method<string, string> DuplexMethod =
		new(MethodType.DuplexStreaming, "svc", "duplexStreaming", Marshaller, Marshaller);

	private static readonly Method<string, string> ClientStreamingMethod =
		new(MethodType.ClientStreaming, "svc", "clientStreaming", Marshaller, Marshaller);

	private sealed class RecordingCallInvoker : CallInvoker {
		public CallOptions LastOptions { get; private set; }

		public override TResponse BlockingUnaryCall<TRequest, TResponse>(
			Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) {
			LastOptions = options;
			return default!;
		}

		public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
			Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) {
			LastOptions = options;
			return new(Task.FromResult<TResponse>(default!), EmptyHeaders, Success, EmptyTrailers, Nothing);
		}

		public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
			Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) {
			LastOptions = options;
			return new(null!, EmptyHeaders, Success, EmptyTrailers, Nothing);
		}

		public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
			Method<TRequest, TResponse> method, string? host, CallOptions options) {
			LastOptions = options;
			return new(null!, Task.FromResult<TResponse>(default!), EmptyHeaders, Success, EmptyTrailers, Nothing);
		}

		public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
			Method<TRequest, TResponse> method, string? host, CallOptions options) {
			LastOptions = options;
			return new(null!, null!, EmptyHeaders, Success, EmptyTrailers, Nothing);
		}

		private static Task<Metadata> EmptyHeaders => Task.FromResult(new Metadata());
		private static Status Success() => Status.DefaultSuccess;
		private static Metadata EmptyTrailers() => new();
		private static void Nothing() { }
	}
}
